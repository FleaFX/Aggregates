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
    /// Every saga class gets one subscription, with the string form of its
    /// <see cref="SagaContractAttribute"/> as subscription id (or its full type name when it has none).
    /// The subscription routes each event with the saga's own <see cref="ISaga{TSagaState,TEvent}.Correlate"/>.
    /// Each saga's hosted service also runs its <see cref="Scheduler"/>, which waits until the saga
    /// defers its first command with <see cref="DeferredCommandExtensions.DeferUntil"/>.
    /// A saga that implements <see cref="ISaga{TSagaState,TEvent,TDeadline}"/> receives the deadlines it
    /// requests with <see cref="Deadline.At"/> through the same scheduler.
    /// </remarks>
    /// <exception cref="InvalidOperationException">
    /// A class implements <see cref="ISaga{TSagaState,TEvent}"/> more than once, implements
    /// <see cref="ISaga{TSagaState,TEvent,TDeadline}"/> more than once, or two classes share a
    /// subscription id.
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

        // Parked messages — listed, replayed and dismissed through the subscriptions of this host
        builder.Services.TryAddSingleton<IParkedMessages, ParkedMessages>();

        // Subscription checkpointing — positions are written in batches
        builder.Services.TryAddSingleton(new SubscriptionCheckpointOptions());
        builder.Services.TryAddSingleton(TimeProvider.System);

        // Subscription loop — subscribes again after a transient failure
        builder.Services.TryAddSingleton(new SubscriptionResubscribeOptions());
        builder.Services.TryAddSingleton<SubscriptionLoop>();

        // Subscription state — reported by the loop, the retry policy and the scheduler; read by SubscriptionHealthCheck
        builder.Services.TryAddSingleton<SubscriptionMonitor>();

        // Deferred commands — one scheduler serves every saga and policy; the activation is per handler class
        builder.Services.TryAddSingleton<Scheduler>();
        builder.Services.TryAddSingleton(typeof(SchedulerActivation<>));

        var sagas = SubscriptionRegistrations.FindHandlers(options.Types, typeof(ISaga<,>));
        var registeredSagas = new List<(Type StateType, Type EventType, Type SagaType)>();

        foreach (var (sagaType, sagaInterface) in sagas) {
            var stateType = sagaInterface.GetGenericArguments()[0];
            var eventType = sagaInterface.GetGenericArguments()[1];

            // ISaga<,,> inherits ISaga<,>, so FindHandlers sees a saga with two deadline types only once.
            var deadlineInterfaces = SagaDeadlines.InterfacesOf(sagaType);
            if (deadlineInterfaces.Length > 1)
                throw new InvalidOperationException(
                    $"{sagaType.FullName} implements ISaga<TSagaState, TEvent, TDeadline> {deadlineInterfaces.Length} times. " +
                    "A saga implements it once: use a marker interface (or object) as the deadline type to receive several kinds of deadlines.");

            builder.Services.TryAddScoped(sagaType);
            registeredSagas.Add((stateType, eventType, sagaType));

            // Subscription hosted service — one per saga class
            var contract = sagaType.GetCustomAttribute<SagaContractAttribute>();
            var registration = new SubscriptionRegistration(
                SubscriptionRegistrations.GetSubscriptionId(sagaType, contract),
                sagaType,
                eventType,
                contract?.StartFromEnd ?? false,
                contract?.Predecessors) {
                ErrorMode = contract?.OnError ?? SubscriptionErrorMode.Park
            };

            if (!SubscriptionRegistrations.Add(builder.Services, registration))
                continue;

            // One instance per saga class: the hosted service, which also replays its parked messages
            var serviceType = typeof(SagaSubscriptionService<,,>).MakeGenericType(sagaType, stateType, eventType);
            builder.Services.AddKeyedSingleton(serviceType, registration.SubscriptionId, (sp, _) =>
                ActivatorUtilities.CreateInstance(sp, serviceType, registration));
            builder.Services.AddSingleton(typeof(IHostedService), sp => sp.GetRequiredKeyedService(serviceType, registration.SubscriptionId));
            builder.Services.AddSingleton(typeof(IReplayableSubscription), sp => sp.GetRequiredKeyedService(serviceType, registration.SubscriptionId));
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
}

internal sealed class SagasBuilder(
    IServiceCollection services,
    IReadOnlyList<(Type StateType, Type EventType, Type SagaType)> registeredSagas) : ISagasBuilder {
    public IServiceCollection Services => services;
    public IReadOnlyList<(Type StateType, Type EventType, Type SagaType)> RegisteredSagas => registeredSagas;
}
