using System.Runtime.CompilerServices;
using KurrentDB.Client;

namespace Aggregates.KurrentDB;

/// <summary>
/// Reads aggregate events from a KurrentDB stream. The stream name equals
/// <see cref="AggregateIdentifier.ToString"/> of the aggregate identifier.
/// </summary>
sealed class KurrentDbRepository<TState, TEvent>(KurrentDBClient client, KurrentDbOptions options)
    : BaseRepository<TState, TEvent>
    where TState : IState<TState, TEvent> {

    /// <inheritdoc/>
    protected override async IAsyncEnumerable<TEvent> ReadEventsAsync(AggregateIdentifier identifier, [EnumeratorCancellation] CancellationToken cancellationToken = default) {

        var result = client.ReadStreamAsync(
            Direction.Forwards,
            identifier.ToString(),
            StreamPosition.Start,
            cancellationToken: cancellationToken);

        if (await result.ReadState == ReadState.StreamNotFound)
            throw new AggregateRootNotFoundException(identifier);

        var expected = 0UL;
        await foreach (var resolvedEvent in result.WithCancellation(cancellationToken)) {
            var record = resolvedEvent.OriginalEvent;
            var revision = record.EventNumber.ToUInt64();
            // The version of the aggregate counts the events read, so every event must be there.
            if (revision != expected)
                throw new InvalidOperationException(
                    $"Stream '{identifier}' can't be read: the event at revision {expected} is missing (the next one is at {revision}). " +
                    "Events were deleted or the stream was truncated, so the state would miss them.");
            expected++;

            var domainEvent = options.Deserialize!(record.EventType, record.Data);
            if (domainEvent is not TEvent typedEvent)
                throw new InvalidOperationException(
                    $"Stream '{identifier}' can't be read: the event at revision {revision} is stored as '{record.EventType}', " +
                    (domainEvent is null
                        ? "which Deserialize doesn't know. "
                        : $"which deserializes to a {domainEvent.GetType().Name}, not a {typeof(TEvent).Name}. ") +
                    "Every event of an aggregate must stay readable: keep old event versions in the code, with an upgrader to the current one.");
            yield return typedEvent;
        }
    }
}
