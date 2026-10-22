using Aggregates.Subscriptions;

namespace Aggregates.Sagas;

/// <summary>
/// Base class for saga repositories. Mirrors <see cref="BaseRepository{TState,TEvent}"/>:
/// identity-map tracking via the ambient <see cref="UnitOfWork"/>, state rebuilt by
/// replaying persisted events.
/// </summary>
/// <typeparam name="TSagaState">The type of the saga state.</typeparam>
/// <typeparam name="TEvent">The type of the events applicable to this saga.</typeparam>
public abstract class BaseSagaRepository<TSagaState, TEvent> : ISagaRepository<TSagaState, TEvent>
    where TSagaState : IState<TSagaState, TEvent> {

    /// <summary>
    /// The kind of a saga stream entry.
    /// </summary>
    protected enum SagaStreamEntryKind {
        /// <summary>
        /// A <c>$&gt;</c> link to a source event the saga reacted to.
        /// </summary>
        Link,

        /// <summary>
        /// A command the saga deferred (<see cref="ScheduledFormat.CommandRequestEventType"/>) or a deadline it
        /// requested (<see cref="ScheduledFormat.DeadlineRequestEventType"/>): it counts for the version, but
        /// doesn't change the state.
        /// </summary>
        ScheduledRequest
    }

    /// <summary>
    /// One entry of a saga stream: the link it holds and, when the link could be resolved and
    /// deserialized to <typeparamref name="TEvent"/>, the event it points to.
    /// </summary>
    /// <param name="Link">For a link: the source event it points to. For a scheduled request: the entry itself.</param>
    /// <param name="Event">
    /// The linked event, or <see langword="null"/> when the link could not be resolved or does
    /// not hold a <typeparamref name="TEvent"/>; <see langword="null"/> for a scheduled request.
    /// </param>
    protected readonly record struct SagaStreamEntry(EventReference Link, TEvent? Event) {
        /// <summary>
        /// The kind of the entry; <see cref="SagaStreamEntryKind.Link"/> unless created with <see cref="ScheduledRequest"/>.
        /// </summary>
        public SagaStreamEntryKind Kind { get; init; }

        /// <summary>
        /// For a scheduled request made by the reaction to a deadline: that deadline. <see langword="null"/> otherwise.
        /// </summary>
        public EventReference? Cause { get; init; }

        /// <summary>
        /// A scheduled request: a command the saga deferred or a deadline it requested.
        /// </summary>
        /// <param name="request">The saga stream and revision of the request.</param>
        /// <param name="cause">The deadline whose reaction made the request, if any (see <see cref="ScheduledEnvelope.Cause"/>).</param>
        public static SagaStreamEntry ScheduledRequest(EventReference request, EventReference? cause = null) =>
            new(request, default) { Kind = SagaStreamEntryKind.ScheduledRequest, Cause = cause };
    }

    /// <inheritdoc/>
    async ValueTask<SagaRoot<TSagaState, TEvent>?> ISagaRepository<TSagaState, TEvent>.TryGetAsync(AggregateIdentifier sagaId, CancellationToken cancellationToken) {
        var unitOfWork = UnitOfWorkScope.Current?.UnitOfWork ?? new UnitOfWork();

        // Return already-tracked instance (identity map).
        if (unitOfWork.Get(sagaId) is { } existing)
            return (SagaRoot<TSagaState, TEvent>)existing.AggregateRoot;

        try {
            var entries = await ReadStreamAsync(sagaId, cancellationToken).ToArrayAsync(cancellationToken);
            // A scheduled request counts for the version only: the command or deadline it holds runs outside the saga.
            var links = entries.Where(entry => entry.Kind == SagaStreamEntryKind.Link).ToArray();
            // A deadline whose reaction stored requests is handled, like a source the stream links to.
            var causes = entries.Where(entry => entry.Cause is not null).Select(entry => entry.Cause!.Value);
            var root = new SagaRoot<TSagaState, TEvent>(
                links.Aggregate(TSagaState.Initial, (state, entry) => entry.Event is { } @event ? state.Apply(@event) : state),
                new AggregateVersion(entries.Length - 1L),
                links.Select(entry => entry.Link).Concat(causes).ToHashSet());
            unitOfWork.Attach(new Aggregate(sagaId, root));
            return root;
        } catch (AggregateRootNotFoundException) {
            return null;
        }
    }

    /// <inheritdoc/>
    void ISagaRepository<TSagaState, TEvent>.Add(AggregateIdentifier sagaId, SagaRoot<TSagaState, TEvent> sagaRoot) =>
        (UnitOfWorkScope.Current?.UnitOfWork ?? new UnitOfWork()).Attach(new Aggregate(sagaId, sagaRoot));

    /// <summary>
    /// Reads the persisted entries for <paramref name="sagaId"/>: links and scheduled requests
    /// (deferred commands and deadlines),
    /// in stream order. The version of the saga counts every entry, also the ones whose link could
    /// not be resolved.
    /// </summary>
    /// <param name="sagaId">The identifier of the saga to read.</param>
    /// <param name="cancellationToken">A token to cancel the operation.</param>
    /// <exception cref="AggregateRootNotFoundException">
    /// Thrown when no saga exists for <paramref name="sagaId"/>.
    /// </exception>
    /// <exception cref="InvalidOperationException">
    /// Thrown when a link whose target exists can't be read back as a <typeparamref name="TEvent"/>, or the stream misses entries
    /// (it doesn't start at revision 0, or has a gap): an implementation fails instead of skipping the entry.
    /// </exception>
    protected abstract IAsyncEnumerable<SagaStreamEntry> ReadStreamAsync(AggregateIdentifier sagaId, CancellationToken cancellationToken = default);
}
