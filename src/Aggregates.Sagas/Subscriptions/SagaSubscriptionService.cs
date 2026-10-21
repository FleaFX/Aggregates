using Aggregates.Subscriptions;
using Aggregates.Subscriptions.Internal;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace Aggregates.Sagas;

/// <summary>
/// A hosted service that subscribes to an event stream and routes incoming events to the
/// instances of <typeparamref name="TSaga"/>.
/// </summary>
/// <remarks>
/// The subscription runs in a <see cref="SubscriptionLoop"/>, which subscribes again after a
/// transient failure, parks messages that could not be deserialized, and records checkpoints.
/// For each event that matches <typeparamref name="TEvent"/>, the service:
/// <list type="number">
///   <item>Calls <see cref="ISaga{TSagaState,TEvent}.Correlate"/> on an instance of <typeparamref name="TSaga"/> from a fresh DI scope to determine which saga instances are interested. The event's stored metadata is passed directly so the saga can read saga identifiers from it. A failing <c>Correlate</c> is retried and the event parked via <see cref="SubscriptionRetryPolicy"/>; no saga handles it then.</item>
///   <item>Creates a fresh DI scope per saga instance and resolves the handler chain of <typeparamref name="TSaga"/> from it.</item>
///   <item>Opens a <see cref="MetadataScope"/> seeded from the event's stored metadata so that commands dispatched by the saga inherit correlation/causation identifiers.</item>
///   <item>Handles the event for every resolved saga identifier, with automatic retry and parked-message fallback via <see cref="SubscriptionRetryPolicy"/>.</item>
/// </list>
/// When the contract blocks (<see cref="SubscriptionErrorMode.Block"/>), the correlation and each saga
/// instance are retried until they succeed instead of parked.
/// A fresh DI scope is created per saga invocation so that scoped services are never captured
/// as singletons by this long-lived hosted service.
/// Next to the subscription it runs the saga's <see cref="Scheduler"/>, which executes the commands
/// the saga deferred with <see cref="DeferredCommandExtensions.DeferUntil"/> and delivers the deadlines
/// it requested with <see cref="Deadline.At"/> when they are due, with retries and parking under
/// <c>scheduled-{subscription id}</c>.
/// As an <see cref="IReplayableSubscription"/>, it handles a replayed parked event for every saga
/// instance it resolves to, and delivers a replayed parked deadline to its saga, once.
/// </remarks>
/// <typeparam name="TSaga">The saga class.</typeparam>
/// <typeparam name="TSagaState">The saga state type.</typeparam>
/// <typeparam name="TEvent">The event type the saga reacts to.</typeparam>
sealed class SagaSubscriptionService<TSaga, TSagaState, TEvent>(
    SubscriptionLoop loop,
    IServiceScopeFactory scopeFactory,
    SubscriptionRetryPolicy retryPolicy,
    Scheduler scheduler,
    SchedulerActivation<TSaga> activation,
    SubscriptionRegistration registration) : BackgroundService, IReplayableSubscription
    where TSaga : ISaga<TSagaState, TEvent>
    where TSagaState : IState<TSagaState, TEvent> {

    /// <inheritdoc/>
    public SubscriptionRegistration Registration => registration;

    /// <inheritdoc/>
    public async ValueTask HandleAsync(SubscriptionMessage message, CancellationToken cancellationToken = default) {
        if (message.Event is not TEvent typedEvent)
            throw new InvalidOperationException(
                $"Subscription '{registration.SubscriptionId}' handles {typeof(TEvent).Name}, but the event of {message.Source} is a {message.Event?.GetType().Name ?? "null"}.");

        // Every saga the event resolves to: a saga that handled it before skips it, because its stream links the event already.
        foreach (var sagaId in await CorrelateAsync(typedEvent, message.Metadata))
            await HandleSagaAsync(sagaId, typedEvent, message, cancellationToken);
    }

    /// <inheritdoc/>
    public async ValueTask HandleDeadlineAsync(ScheduledDeadline deadline, CancellationToken cancellationToken = default) {
        // The saga stream holds the request, so its name is the saga id: no correlation is needed.
        var sagaId = new AggregateIdentifier(deadline.Request.StreamId);
        await using var scope = scopeFactory.CreateAsyncScope();
        var handler = scope.ServiceProvider.GetRequiredService<LoggingSagaHandler<TSaga, TSagaState, TEvent>>();
        // The commands the saga dispatches, and the requests it stores, carry the correlation of the request.
        await using var metadataScope = new MetadataScope(deadline.Metadata);
        await handler.HandleDeadlineAsync(sagaId, deadline.Payload, deadline.Request, cancellationToken);
    }

    /// <inheritdoc/>
    protected override async Task ExecuteAsync(CancellationToken stoppingToken) {
        using var stop = CancellationTokenSource.CreateLinkedTokenSource(stoppingToken);
        var events = loop.RunAsync(registration, ProcessAsync, stop.Token);
        var deferred = scheduler.RunAsync(registration, activation, DeliverDeadlineAsync, stop.Token);
        // Each ends only when the host stops or after a failure that is not transient; the other stops with it.
        await Task.WhenAny(events, deferred);
        await stop.CancelAsync();
        await Task.WhenAll(events, deferred);
    }

    async ValueTask ProcessAsync(SubscriptionMessage message, CancellationToken cancellationToken) {
        if (message.Event is not TEvent typedEvent)
            return;

        // Correlated in its own retry, so a retry never runs the handlers of earlier sagas again.
        AggregateIdentifier[]? sagaIds = null;
        await retryPolicy.ExecuteAsync(async ct => {
            sagaIds = await CorrelateAsync(typedEvent, message.Metadata);
        }, registration.SubscriptionId, message, registration.ErrorMode, cancellationToken);

        // Null when the correlation failed and the event was parked.
        foreach (var sagaId in sagaIds ?? [])
            await retryPolicy.ExecuteAsync(ct => HandleSagaAsync(sagaId, typedEvent, message, ct),
                registration.SubscriptionId, message, registration.ErrorMode, cancellationToken);
    }

    /// <summary>
    /// The identifiers of the saga instances <paramref name="event"/> correlates to, from an instance of
    /// <typeparamref name="TSaga"/> in a fresh scope, so that the saga can use scoped dependencies.
    /// </summary>
    async ValueTask<AggregateIdentifier[]> CorrelateAsync(TEvent @event, EventMetadata metadata) {
        await using var scope = scopeFactory.CreateAsyncScope();
        return [.. scope.ServiceProvider.GetRequiredService<TSaga>().Correlate(@event, metadata)];
    }

    /// <summary>
    /// Handles <paramref name="event"/> once for the saga with <paramref name="sagaId"/>, in a fresh scope.
    /// </summary>
    async ValueTask HandleSagaAsync(AggregateIdentifier sagaId, TEvent @event, SubscriptionMessage message, CancellationToken cancellationToken) {
        await using var scope = scopeFactory.CreateAsyncScope();
        var handler = scope.ServiceProvider.GetRequiredService<LoggingSagaHandler<TSaga, TSagaState, TEvent>>();
        // Seed the metadata scope with the incoming event's metadata so that commands
        // dispatched by the saga inherit correlation/causation identifiers.
        await using var metadataScope = new MetadataScope(message.Metadata);
        await handler.HandleAsync(sagaId, @event, message.Source, cancellationToken);
    }

    /// <summary>
    /// Delivers a due deadline to the saga instance whose stream holds its request, in a fresh scope and
    /// a metadata scope seeded with the metadata of the request, with retries and parking under the
    /// id of the scheduler.
    /// </summary>
    async ValueTask DeliverDeadlineAsync(ScheduledDeadline deadline, CancellationToken cancellationToken) {
        var message = new SubscriptionMessage(deadline.Payload, deadline.CommitPosition, deadline.Request, deadline.Metadata);
        await retryPolicy.ExecuteAsync(ct => HandleDeadlineAsync(deadline, ct),
            ScheduledFormat.SchedulerStream(registration.SubscriptionId), message, cancellationToken);
    }
}
