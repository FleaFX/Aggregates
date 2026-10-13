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
    /// One entry of a saga stream: the link it holds and, when the link could be resolved and
    /// deserialized to <typeparamref name="TEvent"/>, the event it points to.
    /// </summary>
    /// <param name="Link">Identifies the stored event the entry links to.</param>
    /// <param name="Event">
    /// The linked event, or <see langword="null"/> when the link could not be resolved or does
    /// not hold a <typeparamref name="TEvent"/>.
    /// </param>
    protected readonly record struct SagaStreamEntry(EventReference Link, TEvent? Event);

    /// <inheritdoc/>
    async ValueTask<SagaRoot<TSagaState, TEvent>?> ISagaRepository<TSagaState, TEvent>.TryGetAsync(AggregateIdentifier sagaId, CancellationToken cancellationToken) {
        var unitOfWork = UnitOfWorkScope.Current?.UnitOfWork ?? new UnitOfWork();

        // Return already-tracked instance (identity map).
        if (unitOfWork.Get(sagaId) is { } existing)
            return (SagaRoot<TSagaState, TEvent>)existing.AggregateRoot;

        try {
            var entries = await ReadStreamAsync(sagaId, cancellationToken).ToArrayAsync(cancellationToken);
            var root = new SagaRoot<TSagaState, TEvent>(
                entries.Aggregate(TSagaState.Initial, (state, entry) => entry.Event is { } @event ? state.Apply(@event) : state),
                new AggregateVersion(entries.Length - 1L),
                entries.Select(entry => entry.Link).ToHashSet());
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
    /// Reads the persisted entries for <paramref name="sagaId"/> in chronological order. The
    /// version of the saga counts every entry, also the ones whose link could not be resolved.
    /// </summary>
    /// <param name="sagaId">The identifier of the saga to read.</param>
    /// <param name="cancellationToken">A token to cancel the operation.</param>
    /// <exception cref="AggregateRootNotFoundException">
    /// Thrown when no saga exists for <paramref name="sagaId"/>.
    /// </exception>
    protected abstract IAsyncEnumerable<SagaStreamEntry> ReadStreamAsync(AggregateIdentifier sagaId, CancellationToken cancellationToken = default);
}
