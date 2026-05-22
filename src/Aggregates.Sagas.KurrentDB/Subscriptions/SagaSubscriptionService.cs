using Aggregates.KurrentDB;
using Aggregates.Subscriptions;
using KurrentDB.Client;
using Microsoft.Extensions.Hosting;

namespace Aggregates.Sagas.KurrentDB;

/// <summary>
/// A hosted service that subscribes to the KurrentDB <c>$all</c> stream and routes
/// incoming events to the appropriate saga instances.
/// </summary>
/// <remarks>
/// For each received event the service:
/// <list type="number">
///   <item>Deserializes it using <see cref="KurrentDbOptions.Deserialize"/>; skips the event when the type is unknown.</item>
///   <item>Calls <see cref="ISagaIdResolver{TEvent}.Resolve"/> to determine which saga instances are interested.</item>
///   <item>Calls <see cref="ISagaHandler{TSagaState,TEvent}.HandleAsync"/> for every resolved saga identifier.</item>
///   <item>Stores the stream position as the new checkpoint.</item>
/// </list>
/// System events are excluded from the subscription via <see cref="EventTypeFilter.ExcludeSystemEvents"/>.
/// </remarks>
sealed class SagaSubscriptionService<TSagaState, TEvent>(
    KurrentDBClient client,
    KurrentDbOptions options,
    ISagaIdResolver<TEvent> resolver,
    ISagaHandler<TSagaState, TEvent> handler,
    ICheckpointStore checkpointStore,
    string subscriptionId,
    bool startFromEnd) : BackgroundService
    where TSagaState : IState<TSagaState, TEvent> {

    /// <inheritdoc/>
    protected override async Task ExecuteAsync(CancellationToken stoppingToken) {
        var checkpoint = await checkpointStore.GetAsync(subscriptionId, stoppingToken);

        var fromPosition = (checkpoint, startFromEnd) switch {
            ({ } pos, _) => FromAll.After(new Position(pos, pos)),
            (null, true) => FromAll.End,
            _ => FromAll.Start,
        };

        var filterOptions = new SubscriptionFilterOptions(EventTypeFilter.ExcludeSystemEvents());
        await using var subscription = client.SubscribeToAll(fromPosition, filterOptions: filterOptions, cancellationToken: stoppingToken);

        await foreach (var message in subscription.Messages.WithCancellation(stoppingToken)) {
            if (message is not StreamMessage.Event eventMessage)
                continue;

            var resolvedEvent = eventMessage.ResolvedEvent;
            var commitPosition = resolvedEvent.OriginalEvent.Position.CommitPosition;

            var domainEvent = options.Deserialize!(
                resolvedEvent.OriginalEvent.EventType,
                resolvedEvent.OriginalEvent.Data);

            if (domainEvent is TEvent typedEvent) {
                foreach (var sagaId in resolver.Resolve(typedEvent))
                    await handler.HandleAsync(sagaId, typedEvent, stoppingToken);
            }

            await checkpointStore.StoreAsync(subscriptionId, commitPosition, stoppingToken);
        }
    }
}
