using System.Runtime.CompilerServices;
using Aggregates.MSSP;
using Aggregates.Subscriptions;
using MSSP;

namespace Aggregates.Sagas.MSSP;

/// <summary>
/// Repository implementation that loads sagas from MSSP event streams. The stream name equals
/// <see cref="AggregateIdentifier.ToString"/> of the saga identifier. MSSP has no native link
/// events, so the <c>$&gt;</c> links a saga stream holds are resolved client-side: one bounded
/// parallel read per link, preserving the order of the stream. Unresolved links count for the
/// version but leave the state untouched.
/// </summary>
/// <typeparam name="TSagaState">The saga state type.</typeparam>
/// <typeparam name="TEvent">The event type.</typeparam>
sealed class MsspSagaRepository<TSagaState, TEvent>(IMsspClient client, MsspOptions options) : BaseSagaRepository<TSagaState, TEvent>
    where TSagaState : IState<TSagaState, TEvent> {

    /// <inheritdoc/>
    protected override async IAsyncEnumerable<SagaStreamEntry> ReadStreamAsync(AggregateIdentifier sagaId, [EnumeratorCancellation] CancellationToken cancellationToken = default) {
        var recordedEvents = await client.ReadAsync(sagaId.ToString(), cancellationToken: cancellationToken).ToArrayAsync(cancellationToken: cancellationToken);

        if (recordedEvents.Length == 0)
            throw new AggregateRootNotFoundException(sagaId);

        var entries = new SagaStreamEntry[recordedEvents.Length];
        var links = new List<int>();
        for (var i = 0; i < recordedEvents.Length; i++) {
            var recorded = recordedEvents[i];
            if (recorded.EventType == SagaTimeoutFormat.RequestEventType) {
                entries[i] = SagaStreamEntry.TimeoutRequest(new EventReference(sagaId.ToString(), recorded.Revision));
                continue;
            }
            if (recorded.EventType != EventReference.LinkEventType)
                throw new InvalidOperationException(
                    $"Stream '{sagaId}' is not a saga stream: it contains '{recorded.EventType}' at revision {recorded.Revision}. "
                    + $"Saga streams only hold links and timeout requests; does the resolver of {typeof(TEvent).Name} return an aggregate id?");
            if (!EventReference.TryParseLinkData(recorded.Data.Span, out var link))
                throw new InvalidOperationException($"Stream '{sagaId}' holds an invalid link at revision {recorded.Revision}.");
            entries[i] = new SagaStreamEntry(link, default);
            links.Add(i);
        }

        // Only links need a read; a timeout request holds its own data.
        await Parallel.ForEachAsync(links, new ParallelOptions { MaxDegreeOfParallelism = 8, CancellationToken = cancellationToken }, async (i, token) => {
            var link = entries[i].Link;
            await foreach (var target in client.ReadAsync(link.StreamId, link.Revision, maxCount: 1, cancellationToken: token)) {
                if (target.Revision == link.Revision && options.Deserialize!(target.EventType, target.Data) is TEvent typedEvent)
                    entries[i] = new SagaStreamEntry(link, typedEvent);
                break;
            }
        });

        foreach (var entry in entries)
            yield return entry;
    }
}
