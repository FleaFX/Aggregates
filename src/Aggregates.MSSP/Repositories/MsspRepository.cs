using Aggregates.MSSP.Infrastructure;
using MSSP;

namespace Aggregates.MSSP.Repositories;

sealed class MsspRepository<TState, TEvent>(IMsspClient client, MsspOptions options)
    : BaseRepository<TState, TEvent>
    where TState : IState<TState, TEvent> {

    /// <inheritdoc />
    protected override async IAsyncEnumerable<TEvent> ReadEventsAsync(AggregateIdentifier identifier, CancellationToken cancellationToken = default) {
        var recordedEvents = await client.ReadAsync(identifier.ToString(), cancellationToken: cancellationToken).ToArrayAsync(cancellationToken: cancellationToken);

        if (recordedEvents.Length == 0)
            throw new AggregateRootNotFoundException(identifier);

        foreach (var recordedEvent in recordedEvents) {
            if (options.Deserialize!(
                    recordedEvent.EventType,
                    recordedEvent.Data
                ) is TEvent @event)
                yield return @event;
        }
    }
}
