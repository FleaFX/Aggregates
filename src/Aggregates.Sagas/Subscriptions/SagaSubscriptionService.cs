using Aggregates.Subscriptions;
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
///   <item>Calls <see cref="ISagaIdResolver{TEvent}.Resolve"/> to determine which saga instances are interested. The event's stored metadata is passed directly so resolvers can read saga identifiers from it. A failing resolver is retried and the event parked via <see cref="SubscriptionRetryPolicy"/>; no saga handles it then.</item>
///   <item>Creates a fresh DI scope per saga instance and resolves the handler chain of <typeparamref name="TSaga"/> from it.</item>
///   <item>Opens a <see cref="MetadataScope"/> seeded from the event's stored metadata so that commands dispatched by the saga inherit correlation/causation identifiers.</item>
///   <item>Handles the event for every resolved saga identifier, with automatic retry and parked-message fallback via <see cref="SubscriptionRetryPolicy"/>.</item>
/// </list>
/// A fresh DI scope is created per saga invocation so that scoped services are never captured
/// as singletons by this long-lived hosted service.
/// Next to the subscription it runs the saga's <see cref="Scheduler"/>, which executes the commands
/// the saga deferred with <see cref="DeferredCommandExtensions.DeferUntil"/> and delivers the deadlines
/// it requested with <see cref="Deadline.At"/> when they are due, with retries and parking under
/// <c>scheduled-{subscription id}</c>.
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
    SubscriptionRegistration registration,
    ISagaIdResolver<TEvent> resolver) : BackgroundService
    where TSaga : ISaga<TSagaState, TEvent>
    where TSagaState : IState<TSagaState, TEvent> {

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

        // Resolved in its own retry, so a retry never runs the handlers of earlier sagas again.
        AggregateIdentifier[]? sagaIds = null;
        await retryPolicy.ExecuteAsync(ct => {
            sagaIds = [.. resolver.Resolve(typedEvent, message.Metadata)];
            return ValueTask.CompletedTask;
        }, registration.SubscriptionId, message, cancellationToken);

        // Null when the resolver failed and the event was parked.
        foreach (var sagaId in sagaIds ?? []) {
            await retryPolicy.ExecuteAsync(async ct => {
                await using var scope = scopeFactory.CreateAsyncScope();
                var handler = scope.ServiceProvider.GetRequiredService<LoggingSagaHandler<TSaga, TSagaState, TEvent>>();
                // Seed the metadata scope with the incoming event's metadata so that commands
                // dispatched by the saga inherit correlation/causation identifiers.
                await using var metadataScope = new MetadataScope(message.Metadata);
                await handler.HandleAsync(sagaId, typedEvent, message.Source, ct);
            }, registration.SubscriptionId, message, cancellationToken);
        }
    }

    /// <summary>
    /// Delivers a due deadline to the saga instance whose stream holds its request, in a fresh scope and
    /// a metadata scope seeded with the metadata of the request, with retries and parking under the
    /// id of the scheduler.
    /// </summary>
    async ValueTask DeliverDeadlineAsync(ScheduledDeadline deadline, CancellationToken cancellationToken) {
        // The saga stream holds the request, so its name is the saga id: no resolver is needed.
        var sagaId = new AggregateIdentifier(deadline.Request.StreamId);
        var message = new SubscriptionMessage(deadline.Payload, deadline.CommitPosition, deadline.Request, deadline.Metadata);
        await retryPolicy.ExecuteAsync(async ct => {
            await using var scope = scopeFactory.CreateAsyncScope();
            var handler = scope.ServiceProvider.GetRequiredService<LoggingSagaHandler<TSaga, TSagaState, TEvent>>();
            // The commands the saga dispatches, and the requests it stores, carry the correlation of the request.
            await using var metadataScope = new MetadataScope(deadline.Metadata);
            await handler.HandleDeadlineAsync(sagaId, deadline.Payload, deadline.Request, ct);
        }, ScheduledFormat.SchedulerStream(registration.SubscriptionId), message, cancellationToken);
    }
}
