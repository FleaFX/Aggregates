using System.Runtime.CompilerServices;
using MSSP;

namespace Aggregates.MSSP;

/// <summary>
/// Repository implementation that loads aggregates from MSSP event streams.
/// </summary>
/// <typeparam name="TState">The aggregate state type.</typeparam>
/// <typeparam name="TEvent">The event type.</typeparam>
sealed class MsspRepository<TState, TEvent>(IMsspClient client, MsspOptions options)
    : BaseRepository<TState, TEvent>
    where TState : IState<TState, TEvent> {

    /// <inheritdoc />
    protected override async IAsyncEnumerable<TEvent> ReadEventsAsync(AggregateIdentifier identifier, [EnumeratorCancellation] CancellationToken cancellationToken = default) {
        var recordedEvents = await client.ReadAsync(identifier.ToString(), cancellationToken: cancellationToken).ToArrayAsync(cancellationToken: cancellationToken);

        if (recordedEvents.Length == 0)
            throw new AggregateRootNotFoundException(identifier);

        var expected = 0UL;
        foreach (var recordedEvent in recordedEvents) {
            var revision = recordedEvent.Revision;
            // The version of the aggregate counts the events read, so every event must be there.
            if (revision != expected)
                throw new InvalidOperationException(
                    $"Stream '{identifier}' can't be read: the event at revision {expected} is missing (the next one is at {revision}). " +
                    "Events were deleted or the stream was truncated, so the state would miss them.");
            expected++;

            var domainEvent = options.Deserialize!(recordedEvent.EventType, recordedEvent.Data);
            if (domainEvent is not TEvent typedEvent)
                throw new InvalidOperationException(
                    $"Stream '{identifier}' can't be read: the event at revision {revision} is stored as '{recordedEvent.EventType}', " +
                    (domainEvent is null
                        ? "which Deserialize doesn't know. "
                        : $"which deserializes to a {domainEvent.GetType().Name}, not a {typeof(TEvent).Name}. ") +
                    "Every event of an aggregate must stay readable: keep old event versions in the code, with an upgrader to the current one.");
            yield return typedEvent;
        }
    }
}
