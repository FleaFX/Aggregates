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

        await foreach (var resolvedEvent in result.WithCancellation(cancellationToken)) {
            var domainEvent = options.Deserialize!(
                resolvedEvent.OriginalEvent.EventType,
                resolvedEvent.OriginalEvent.Data);

            if (domainEvent is TEvent typedEvent)
                yield return typedEvent;
        }
    }
}
