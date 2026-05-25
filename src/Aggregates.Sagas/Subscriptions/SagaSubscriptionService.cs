using Aggregates.Subscriptions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace Aggregates.Sagas;

/// <summary>
/// A hosted service that subscribes to an event stream and routes incoming events to the
/// appropriate saga instances.
/// </summary>
/// <remarks>
/// For each received event the service:
/// <list type="number">
///   <item>Calls <see cref="ISagaIdResolver{TEvent}.Resolve"/> to determine which saga instances are interested. The event's stored metadata is passed directly so resolvers can read saga identifiers from it.</item>
///   <item>Creates a fresh DI scope per saga instance and resolves <see cref="ISagaHandler{TSagaState,TEvent}"/> from it.</item>
///   <item>Opens a <see cref="MetadataScope"/> seeded from the event's stored metadata so that commands dispatched by the saga inherit correlation/causation identifiers.</item>
///   <item>Calls <see cref="ISagaHandler{TSagaState,TEvent}.HandleAsync"/> for every resolved saga identifier, with automatic retry and parked-message fallback via <see cref="SubscriptionRetryPolicy"/>.</item>
///   <item>Stores the stream position as the new checkpoint.</item>
/// </list>
/// A fresh DI scope is created per saga invocation so that scoped services are never captured
/// as singletons by this long-lived hosted service.
/// </remarks>
sealed class SagaSubscriptionService<TSagaState, TEvent>(
    ISubscriptionFactory subscriptionFactory,
    ISagaIdResolver<TEvent> resolver,
    IServiceScopeFactory scopeFactory,
    ICheckpointStore checkpointStore,
    SubscriptionRetryPolicy retryPolicy,
    string subscriptionId,
    bool startFromEnd) : BackgroundService
    where TSagaState : IState<TSagaState, TEvent> {

    /// <inheritdoc/>
    protected override async Task ExecuteAsync(CancellationToken stoppingToken) {
        var checkpoint = await checkpointStore.GetAsync(subscriptionId, stoppingToken);

        await using var subscription = subscriptionFactory.Subscribe(checkpoint, startFromEnd, stoppingToken);

        await foreach (var message in subscription.WithCancellation(stoppingToken)) {
            if (message.Event is TEvent typedEvent) {
                foreach (var sagaId in resolver.Resolve(typedEvent, message.Metadata)) {
                    await retryPolicy.ExecuteAsync(async ct => {
                        await using var scope = scopeFactory.CreateAsyncScope();
                        var handler = scope.ServiceProvider.GetRequiredService<ISagaHandler<TSagaState, TEvent>>();
                        // Seed the metadata scope with the incoming event's metadata so that commands
                        // dispatched by the saga inherit correlation/causation identifiers.
                        await using var metadataScope = new MetadataScope(message.Metadata);
                        await handler.HandleAsync(sagaId, typedEvent, ct);
                    }, subscriptionId, message, stoppingToken);
                }
            }

            await checkpointStore.StoreAsync(subscriptionId, message.CommitPosition, stoppingToken);
        }
    }
}
