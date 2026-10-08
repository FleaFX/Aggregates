using System.Reflection;
using Aggregates.Subscriptions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;

namespace Aggregates.Sagas;

/// <summary>
/// Extension methods for registering <c>Aggregates.Sagas</c> with an
/// <see cref="Microsoft.Extensions.DependencyInjection.IServiceCollection"/>.
/// </summary>
public static class ServiceCollectionExtensions {
    /// <summary>
    /// Adds the <c>Aggregates.Sagas</c> package to the service collection.
    /// Call after <see cref="Aggregates.ServiceCollectionExtensions.AddAggregates"/>.
    /// </summary>
    /// <param name="builder">The aggregates builder returned by <c>AddAggregates</c>.</param>
    /// <param name="configure">
    /// Optional configuration callback. Use <see cref="SagasOptions.ScanAssemblies"/> to
    /// automatically register a handler for every <see cref="ISaga{TSagaState,TEvent}"/>
    /// implementation found in those assemblies.
    /// Use <see cref="SagasOptions.ScanTypes"/> instead to register an explicit set of types.
    /// </param>
    /// <remarks>
    /// Every saga class with a resolver gets one subscription, with the string form of its
    /// <see cref="SagaContractAttribute"/> as subscription id (or its full type name when it has none).
    /// The resolver registered for the saga with <see cref="SagasOptions.WithResolver{TSaga,TEvent}(ISagaIdResolver{TEvent})"/>
    /// takes precedence over the one registered for its event type.
    /// </remarks>
    /// <exception cref="InvalidOperationException">
    /// A class implements <see cref="ISaga{TSagaState,TEvent}"/> more than once, two classes share a
    /// subscription id, or a saga resolver doesn't match a scanned saga.
    /// </exception>
    public static ISagasBuilder AddSagas(this IAggregatesBuilder builder, Action<SagasOptions>? configure = null) {
        var options = new SagasOptions();
        configure?.Invoke(options);

        // Handler chain per saga class: LoggingSagaHandler<,,> → RetrySagaHandler<,,> → UnitOfWorkAwareSagaHandler<,,> → SagaHandler<,,> → TSaga
        builder.Services.TryAddScoped(typeof(LoggingSagaHandler<,,>));
        builder.Services.TryAddScoped(typeof(RetrySagaHandler<,,>));
        builder.Services.TryAddScoped(typeof(UnitOfWorkAwareSagaHandler<,,>));
        builder.Services.TryAddScoped(typeof(SagaHandler<,,>));

        // Subscription error handling — transport packages register their own IParkedMessageSink;
        // LoggingParkedMessageSink is the fallback for dev/test scenarios without a transport.
        builder.Services.TryAddSingleton<IParkedMessageSink, LoggingParkedMessageSink>();
        builder.Services.TryAddSingleton(new SubscriptionErrorHandlingOptions());
        builder.Services.TryAddSingleton<SubscriptionRetryPolicy>();

        // Subscription checkpointing — positions are written in batches
        builder.Services.TryAddSingleton(new SubscriptionCheckpointOptions());
        builder.Services.TryAddSingleton(TimeProvider.System);

        // Subscription loop — subscribes again after a transient failure
        builder.Services.TryAddSingleton(new SubscriptionResubscribeOptions());
        builder.Services.TryAddSingleton<SubscriptionLoop>();

        // ISagaIdResolver<TEvent> registrations, shared by every saga on TEvent
        foreach (var (eventType, resolver) in options.Resolvers)
            builder.Services.TryAdd(ServiceDescriptor.Singleton(typeof(ISagaIdResolver<>).MakeGenericType(eventType), resolver));

        var sagas = SubscriptionRegistrations.FindHandlers(options.Types, typeof(ISaga<,>));
        var sagaResolvers = SagaResolvers(options, sagas);
        var registeredSagas = new List<(Type StateType, Type EventType, Type SagaType)>();

        foreach (var (sagaType, sagaInterface) in sagas) {
            var stateType = sagaInterface.GetGenericArguments()[0];
            var eventType = sagaInterface.GetGenericArguments()[1];

            builder.Services.TryAddScoped(sagaType);
            registeredSagas.Add((stateType, eventType, sagaType));

            // Subscription hosted service — one per saga class that has a resolver
            var resolverType = typeof(ISagaIdResolver<>).MakeGenericType(eventType);
            var sagaResolver = sagaResolvers.GetValueOrDefault(sagaType);
            if (sagaResolver is null && !builder.Services.Any(sd => sd.ServiceType == resolverType))
                continue;

            var contract = sagaType.GetCustomAttribute<SagaContractAttribute>();
            var registration = new SubscriptionRegistration(
                SubscriptionRegistrations.GetSubscriptionId(sagaType, contract),
                sagaType,
                eventType,
                contract?.StartFromEnd ?? false);

            if (!SubscriptionRegistrations.Add(builder.Services, registration))
                continue;

            var serviceType = typeof(SagaSubscriptionService<,,>).MakeGenericType(sagaType, stateType, eventType);
            builder.Services.AddSingleton(typeof(IHostedService), sp =>
                ActivatorUtilities.CreateInstance(sp, serviceType, registration, sagaResolver ?? sp.GetRequiredService(resolverType)));
        }

        return new SagasBuilder(builder.Services, registeredSagas);
    }

    /// <summary>
    /// Registers <paramref name="openGenericRepositoryType"/> as the
    /// <see cref="ISagaRepository{TSagaState,TEvent}"/> implementation.
    /// Called by storage integration packages (e.g. <c>Aggregates.Sagas.KurrentDB</c>).
    /// </summary>
    public static ISagasBuilder UseSagaRepository(this ISagasBuilder builder, Type openGenericRepositoryType) {
        builder.Services.TryAddScoped(typeof(ISagaRepository<,>), openGenericRepositoryType);
        return builder;
    }

    // The resolvers registered per saga class, checked against the scanned sagas.
    static Dictionary<Type, object> SagaResolvers(SagasOptions options, IReadOnlyList<(Type HandlerType, Type HandlerInterface)> sagas) {
        var resolvers = new Dictionary<Type, object>();
        foreach (var (sagaType, eventType, resolver) in options.SagaResolvers) {
            var saga = sagas.FirstOrDefault(s => s.HandlerType == sagaType);
            if (saga.HandlerType is null)
                throw new InvalidOperationException(
                    $"A resolver is registered for {sagaType.FullName}, which is not one of the scanned sagas.");

            var sagaEventType = saga.HandlerInterface.GetGenericArguments()[1];
            if (!typeof(ISagaIdResolver<>).MakeGenericType(sagaEventType).IsInstanceOfType(resolver))
                throw new InvalidOperationException(
                    $"The resolver registered for {sagaType.FullName} resolves {eventType.Name}, " +
                    $"but the saga reacts to {sagaEventType.Name}.");

            if (!resolvers.TryAdd(sagaType, resolver))
                throw new InvalidOperationException($"More than one resolver is registered for {sagaType.FullName}.");
        }
        return resolvers;
    }
}

internal sealed class SagasBuilder(
    IServiceCollection services,
    IReadOnlyList<(Type StateType, Type EventType, Type SagaType)> registeredSagas) : ISagasBuilder {
    public IServiceCollection Services => services;
    public IReadOnlyList<(Type StateType, Type EventType, Type SagaType)> RegisteredSagas => registeredSagas;
}
