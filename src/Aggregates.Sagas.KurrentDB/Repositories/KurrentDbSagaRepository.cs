using System.Runtime.CompilerServices;
using Aggregates.KurrentDB;
using Aggregates.Subscriptions;
using KurrentDB.Client;

namespace Aggregates.Sagas.KurrentDB;

/// <summary>
/// Reads saga streams from KurrentDB. The stream name equals
/// <see cref="AggregateIdentifier.ToString"/> of the saga identifier. The stream is read with
/// <c>resolveLinkTos</c>, so the <c>$&gt;</c> links a saga stream holds are resolved to the
/// events they point to; unresolved links count for the version but leave the state untouched.
/// Timeout requests count for the version; the commands they plan are executed by the timeout
/// scheduler.
/// </summary>
sealed class KurrentDbSagaRepository<TSagaState, TEvent>(KurrentDBClient client, KurrentDbOptions options) : BaseSagaRepository<TSagaState, TEvent>
    where TSagaState : IState<TSagaState, TEvent> {

    /// <inheritdoc/>
    protected override async IAsyncEnumerable<SagaStreamEntry> ReadStreamAsync(AggregateIdentifier sagaId, [EnumeratorCancellation] CancellationToken cancellationToken = default) {
        var result = client.ReadStreamAsync(
            Direction.Forwards,
            sagaId.ToString(),
            StreamPosition.Start,
            resolveLinkTos: true,
            cancellationToken: cancellationToken);

        if (await result.ReadState == ReadState.StreamNotFound)
            throw new AggregateRootNotFoundException(sagaId);

        await foreach (var resolvedEvent in result.WithCancellation(cancellationToken)) {
            // The entry in the saga stream: a link (resolved or not) or a timeout request.
            var entry = resolvedEvent.OriginalEvent;
            var revision = entry.EventNumber.ToUInt64();
            if (entry.EventType == SagaTimeoutFormat.RequestEventType) {
                yield return SagaStreamEntry.TimeoutRequest(new EventReference(sagaId.ToString(), revision));
                continue;
            }
            if (entry.EventType != EventReference.LinkEventType)
                throw new InvalidOperationException(
                    $"Stream '{sagaId}' is not a saga stream: it contains '{entry.EventType}' at revision {revision}. "
                    + $"Saga streams only hold links and timeout requests; does the resolver of {typeof(TEvent).Name} return an aggregate id?");

            if (!EventReference.TryParseLinkData(resolvedEvent.OriginalEvent.Data.Span, out var link))
                throw new InvalidOperationException(
                    $"Stream '{sagaId}' holds an invalid link at revision {resolvedEvent.OriginalEvent.EventNumber.ToUInt64()}.");

            // A resolved link exposes the event it points to; an unresolved link (deleted or
            // scavenged source stream) leaves the state untouched but still counts for the version.
            TEvent? @event = default;
            if (resolvedEvent.Link is not null && resolvedEvent.Event is { } target &&
                options.Deserialize!(target.EventType, target.Data) is TEvent typedEvent)
                @event = typedEvent;

            yield return new SagaStreamEntry(link, @event);
        }
    }
}
