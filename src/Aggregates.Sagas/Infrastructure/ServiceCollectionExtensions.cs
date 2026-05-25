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
    /// </param>
    public static ISagasBuilder AddSagas(this IAggregatesBuilder builder, Action<SagasOptions>? configure = null) {
        var options = new SagasOptions();
        configure?.Invoke(options);

        // Decorator chain (open-generic): ISagaHandler<,> → LoggingSagaHandler<,> → RetrySagaHandler<,> → UnitOfWorkAwareSagaHandler<,>
        builder.Services.TryAddScoped(typeof(UnitOfWorkAwareSagaHandler<,>));
        builder.Services.TryAddScoped(typeof(RetrySagaHandler<,>));
        builder.Services.TryAddScoped(typeof(ISagaHandler<,>), typeof(LoggingSagaHandler<,>));

        // Subscription error handling — transport packages register their own IParkedMessageSink;
        // LoggingParkedMessageSink is the fallback for dev/test scenarios without a transport.
        builder.Services.TryAddSingleton<IParkedMessageSink, LoggingParkedMessageSink>();
        builder.Services.TryAddSingleton(new SubscriptionErrorHandlingOptions());
        builder.Services.TryAddSingleton<SubscriptionRetryPolicy>();

        // Per ISaga<,> implementation: register the saga class and its concrete handler
        var registeredSagas = new List<(Type StateType, Type EventType, Type SagaType)>();

        foreach (var (sagaType, stateType, eventType) in
            from assembly in options.Assemblies
            from type in assembly.GetTypes()
            where !type.IsAbstract
            from @interface in type.GetInterfaces()
            where @interface.IsGenericType
            where @interface.GetGenericTypeDefinition() == typeof(ISaga<,>)
            let typeArgs = @interface.GetGenericArguments()
            select (sagaType: type, stateType: typeArgs[0], eventType: typeArgs[1])) {

            builder.Services.TryAddScoped(
                typeof(ISaga<,>).MakeGenericType(stateType, eventType),
                sagaType);

            builder.Services.TryAddScoped(
                typeof(SagaHandler<,>).MakeGenericType(stateType, eventType),
                typeof(SagaHandler<,,>).MakeGenericType(sagaType, stateType, eventType));

            registeredSagas.Add((stateType, eventType, sagaType));
        }

        // ISagaIdResolver<TEvent> registrations
        foreach (var (eventType, resolver) in options.Resolvers)
            builder.Services.TryAdd(ServiceDescriptor.Singleton(typeof(ISagaIdResolver<>).MakeGenericType(eventType), resolver));

        // Subscription hosted service — one per saga type that has a registered resolver
        foreach (var (stateType, eventType, sagaType) in registeredSagas) {
            var resolverType = typeof(ISagaIdResolver<>).MakeGenericType(eventType);
            if (!builder.Services.Any(sd => sd.ServiceType == resolverType))
                continue;

            var subscriptionId = GetSubscriptionId(sagaType);
            var startFromEnd = GetStartFromEnd(sagaType);
            var serviceType = typeof(SagaSubscriptionService<,>).MakeGenericType(stateType, eventType);

            builder.Services.AddSingleton(typeof(IHostedService), sp =>
                ActivatorUtilities.CreateInstance(sp, serviceType, subscriptionId, startFromEnd));
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

    static string GetSubscriptionId(Type sagaType) {
        var attr = sagaType.GetCustomAttribute<SagaContractAttribute>();
        return attr?.ToString() ?? sagaType.FullName ?? sagaType.Name;
    }

    static bool GetStartFromEnd(Type sagaType) =>
        sagaType.GetCustomAttribute<SagaContractAttribute>()?.StartFromEnd ?? false;
}

internal sealed class SagasBuilder(
    IServiceCollection services,
    IReadOnlyList<(Type StateType, Type EventType, Type SagaType)> registeredSagas) : ISagasBuilder {
    public IServiceCollection Services => services;
    public IReadOnlyList<(Type StateType, Type EventType, Type SagaType)> RegisteredSagas => registeredSagas;
}
