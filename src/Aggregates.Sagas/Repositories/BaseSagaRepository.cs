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

    /// <inheritdoc/>
    async ValueTask<SagaRoot<TSagaState, TEvent>?> ISagaRepository<TSagaState, TEvent>.TryGetAsync(AggregateIdentifier sagaId, CancellationToken cancellationToken) {
        var unitOfWork = UnitOfWorkScope.Current?.UnitOfWork ?? new UnitOfWork();

        // Return already-tracked instance (identity map).
        if (unitOfWork.Get(sagaId) is { } existing)
            return (SagaRoot<TSagaState, TEvent>)existing.AggregateRoot;

        try {
            var events = await ReadEventsAsync(sagaId, cancellationToken).ToArrayAsync(cancellationToken);
            var root = new SagaRoot<TSagaState, TEvent>(
                events.Aggregate(TSagaState.Initial, (state, @event) => state.Apply(@event)),
                new AggregateVersion(events.Length - 1L));
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
    /// Reads the persisted events for <paramref name="sagaId"/> in chronological order.
    /// </summary>
    /// <param name="sagaId">The identifier of the saga to read.</param>
    /// <param name="cancellationToken">A token to cancel the operation.</param>
    /// <exception cref="AggregateRootNotFoundException">
    /// Thrown when no saga exists for <paramref name="sagaId"/>.
    /// </exception>
    protected abstract IAsyncEnumerable<TEvent> ReadEventsAsync(AggregateIdentifier sagaId, CancellationToken cancellationToken = default);
}
