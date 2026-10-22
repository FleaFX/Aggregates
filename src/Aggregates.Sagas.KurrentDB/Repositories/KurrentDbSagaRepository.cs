using System.Runtime.CompilerServices;
using Aggregates.KurrentDB;
using Aggregates.Subscriptions;
using KurrentDB.Client;

namespace Aggregates.Sagas.KurrentDB;

/// <summary>
/// Reads saga streams from KurrentDB. The stream name equals
/// <see cref="AggregateIdentifier.ToString"/> of the saga identifier. The stream is read with
/// <c>resolveLinkTos</c>, so the <c>$&gt;</c> links a saga stream holds are resolved to the
/// events they point to; unresolved links count for the version but leave the state untouched,
/// and a link whose target exists but can't be read fails the load.
/// Deferred command and deadline requests count for the version; the scheduler executes or delivers them.
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

        var expected = 0UL;
        await foreach (var resolvedEvent in result.WithCancellation(cancellationToken)) {
            // The entry in the saga stream: a link (resolved or not), or a request (a deferred command or a deadline).
            var entry = resolvedEvent.OriginalEvent;
            var revision = entry.EventNumber.ToUInt64();
            // The version of the saga counts the entries read, so every entry must be there.
            if (revision != expected)
                throw new InvalidOperationException(
                    $"Saga stream '{sagaId}' can't be read: the event at revision {expected} is missing (the next one is at {revision}). " +
                    "Events were deleted or the stream was truncated, so the state would miss them.");
            expected++;
            if (entry.EventType is ScheduledFormat.CommandRequestEventType or ScheduledFormat.DeadlineRequestEventType) {
                // The cause marks a deadline whose reaction is stored; an unreadable request has none.
                var cause = ScheduledFormat.TryReadRequest(entry.Data, out var envelope) ? envelope.Cause : null;
                yield return SagaStreamEntry.ScheduledRequest(new EventReference(sagaId.ToString(), revision), cause);
                continue;
            }
            if (entry.EventType != EventReference.LinkEventType)
                throw new InvalidOperationException(
                    $"Stream '{sagaId}' is not a saga stream: it contains '{entry.EventType}' at revision {revision}. "
                    + $"Saga streams only hold links and scheduled requests; does a saga on {typeof(TEvent).Name} correlate its events to an aggregate id?");

            if (!EventReference.TryParseLinkData(resolvedEvent.OriginalEvent.Data.Span, out var link))
                throw new InvalidOperationException(
                    $"Stream '{sagaId}' holds an invalid link at revision {resolvedEvent.OriginalEvent.EventNumber.ToUInt64()}.");

            // A resolved link exposes the event it points to; an unresolved link (deleted or
            // scavenged source stream) leaves the state untouched but still counts for the version.
            TEvent? @event = default;
            if (resolvedEvent.Link is not null && resolvedEvent.Event is { } target) {
                var domainEvent = options.Deserialize!(target.EventType, target.Data);
                if (domainEvent is not TEvent typedEvent)
                    throw new InvalidOperationException(
                        $"Saga stream '{sagaId}' can't be read: its link at revision {revision} points to {link}, stored as '{target.EventType}', " +
                        (domainEvent is null
                            ? "which Deserialize doesn't know. "
                            : $"which deserializes to a {domainEvent.GetType().Name}, not a {typeof(TEvent).Name}. ") +
                        "Every event a saga reacted to must stay readable: keep old event versions in the code, with an upgrader to the current one.");
                @event = typedEvent;
            }

            yield return new SagaStreamEntry(link, @event);
        }
    }
}
