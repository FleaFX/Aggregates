using Aggregates.Subscriptions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace Aggregates.Sagas;

/// <summary>
/// A hosted service that subscribes to an event stream and routes incoming events to the
/// appropriate saga instances.
/// </summary>
/// <remarks>
/// The subscription runs in a <see cref="SubscriptionLoop"/>, which subscribes again after a
/// transient failure, parks messages that could not be deserialized, and records checkpoints.
/// For each event that matches <typeparamref name="TEvent"/>, the service:
/// <list type="number">
///   <item>Calls <see cref="ISagaIdResolver{TEvent}.Resolve"/> to determine which saga instances are interested. The event's stored metadata is passed directly so resolvers can read saga identifiers from it. A failing resolver is retried and the event parked via <see cref="SubscriptionRetryPolicy"/>; no saga handles it then.</item>
///   <item>Creates a fresh DI scope per saga instance and resolves <see cref="ISagaHandler{TSagaState,TEvent}"/> from it.</item>
///   <item>Opens a <see cref="MetadataScope"/> seeded from the event's stored metadata so that commands dispatched by the saga inherit correlation/causation identifiers.</item>
///   <item>Calls <see cref="ISagaHandler{TSagaState,TEvent}.HandleAsync"/> for every resolved saga identifier, with automatic retry and parked-message fallback via <see cref="SubscriptionRetryPolicy"/>.</item>
/// </list>
/// A fresh DI scope is created per saga invocation so that scoped services are never captured
/// as singletons by this long-lived hosted service.
/// </remarks>
sealed class SagaSubscriptionService<TSagaState, TEvent>(
    SubscriptionLoop loop,
    ISagaIdResolver<TEvent> resolver,
    IServiceScopeFactory scopeFactory,
    SubscriptionRetryPolicy retryPolicy,
    string subscriptionId,
    bool startFromEnd) : BackgroundService
    where TSagaState : IState<TSagaState, TEvent> {

    /// <inheritdoc/>
    protected override Task ExecuteAsync(CancellationToken stoppingToken) =>
        loop.RunAsync(subscriptionId, startFromEnd, ProcessAsync, stoppingToken);

    async ValueTask ProcessAsync(SubscriptionMessage message, CancellationToken cancellationToken) {
        if (message.Event is not TEvent typedEvent)
            return;

        // Resolved in its own retry, so a retry never runs the handlers of earlier sagas again.
        AggregateIdentifier[]? sagaIds = null;
        await retryPolicy.ExecuteAsync(ct => {
            sagaIds = [.. resolver.Resolve(typedEvent, message.Metadata)];
            return ValueTask.CompletedTask;
        }, subscriptionId, message, cancellationToken);

        // Null when the resolver failed and the event was parked.
        foreach (var sagaId in sagaIds ?? []) {
            await retryPolicy.ExecuteAsync(async ct => {
                await using var scope = scopeFactory.CreateAsyncScope();
                var handler = scope.ServiceProvider.GetRequiredService<ISagaHandler<TSagaState, TEvent>>();
                // Seed the metadata scope with the incoming event's metadata so that commands
                // dispatched by the saga inherit correlation/causation identifiers.
                await using var metadataScope = new MetadataScope(message.Metadata);
                await handler.HandleAsync(sagaId, typedEvent, ct);
            }, subscriptionId, message, cancellationToken);
        }
    }
}
