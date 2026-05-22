using System.Runtime.CompilerServices;
using Aggregates.KurrentDB;
using KurrentDB.Client;

namespace Aggregates.Sagas.KurrentDB;

/// <summary>
/// Reads saga events from a KurrentDB stream. The stream name equals
/// <see cref="AggregateIdentifier.ToString"/> of the saga identifier.
/// </summary>
sealed class KurrentDbSagaRepository<TSagaState, TEvent>(KurrentDBClient client, KurrentDbOptions options) : BaseSagaRepository<TSagaState, TEvent>
    where TSagaState : IState<TSagaState, TEvent> {

    /// <inheritdoc/>
    protected override async IAsyncEnumerable<TEvent> ReadEventsAsync(AggregateIdentifier sagaId, [EnumeratorCancellation] CancellationToken cancellationToken = default) {
        var result = client.ReadStreamAsync(
            Direction.Forwards,
            sagaId.ToString(),
            StreamPosition.Start,
            cancellationToken: cancellationToken);

        if (await result.ReadState == ReadState.StreamNotFound)
            throw new AggregateRootNotFoundException(sagaId);

        await foreach (var resolvedEvent in result.WithCancellation(cancellationToken)) {
            var domainEvent = options.Deserialize!(
                resolvedEvent.OriginalEvent.EventType,
                resolvedEvent.OriginalEvent.Data);

            if (domainEvent is TEvent typedEvent)
                yield return typedEvent;
        }
    }
}
