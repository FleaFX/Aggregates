using System.Runtime.CompilerServices;
using Aggregates.MSSP;
using MSSP;

namespace Aggregates.Sagas.MSSP;

/// <summary>
/// Repository implementation that loads sagas from MSSP event streams.
/// </summary>
/// <typeparam name="TSagaState">The saga state type.</typeparam>
/// <typeparam name="TEvent">The event type.</typeparam>
sealed class MsspSagaRepository<TSagaState, TEvent>(IMsspClient client, MsspOptions options) : BaseSagaRepository<TSagaState, TEvent>
    where TSagaState : IState<TSagaState, TEvent> {

    /// <inheritdoc/>
    protected override async IAsyncEnumerable<TEvent> ReadEventsAsync(AggregateIdentifier sagaId, [EnumeratorCancellation] CancellationToken cancellationToken = default) {
        var recordedEvents = await client.ReadAsync(sagaId.ToString(), cancellationToken: cancellationToken).ToArrayAsync(cancellationToken: cancellationToken);

        if (recordedEvents.Length == 0)
            throw new AggregateRootNotFoundException(sagaId);

        foreach (var recordedEvent in recordedEvents) {
            if (options.Deserialize!(
                    recordedEvent.EventType,
                    recordedEvent.Data
                ) is TEvent @event)
                yield return @event;
        }
    }
}
