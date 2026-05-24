namespace Aggregates;

/// <summary>
/// Base class for aggregate repositories. Handles identity-map tracking via the ambient
/// <see cref="UnitOfWork"/> (from <see cref="UnitOfWorkScope.Current"/>, if active) and
/// rebuilds aggregate state by replaying persisted events.
/// </summary>
/// <typeparam name="TState">The type of the aggregate state.</typeparam>
/// <typeparam name="TEvent">The type of the domain events.</typeparam>
public abstract class BaseRepository<TState, TEvent> : IRepository<TState, TEvent>
    where TState : IState<TState, TEvent> {

    /// <inheritdoc />
    async ValueTask<EntityRoot<TState, TEvent>?> IRepository<TState, TEvent>.TryGetEntityRootAsync(AggregateIdentifier identifier, CancellationToken cancellationToken) {
        var unitOfWork = UnitOfWorkScope.Current?.UnitOfWork ?? new UnitOfWork();

        // Return already-tracked instance (identity map).
        if (unitOfWork.Get(identifier) is { } existing)
            return (EntityRoot<TState, TEvent>)existing.AggregateRoot;
        
        try {
            var events = await ReadEventsAsync(identifier, cancellationToken).ToArrayAsync(cancellationToken: cancellationToken);
            var root = new EntityRoot<TState, TEvent>(events.Aggregate(TState.Initial, (current, @event) => current.Apply(@event)), new AggregateVersion(events.Length - 1L));
            unitOfWork.Attach(new Aggregate(identifier, root));
            return root;
        } catch (AggregateRootNotFoundException) {
            return null;
        }
    }

    /// <inheritdoc />
    void IRepository<TState, TEvent>.Add(AggregateIdentifier identifier, EntityRoot<TState, TEvent> entityRoot) =>
        (UnitOfWorkScope.Current?.UnitOfWork ?? new UnitOfWork()).Attach(new Aggregate(identifier, entityRoot));

    /// <summary>
    /// Reads the persisted events for <paramref name="identifier"/> in chronological order.
    /// </summary>
    /// <param name="identifier">The identifier of the aggregate to read.</param>
    /// <param name="cancellationToken">A token to cancel the operation.</param>
    /// <exception cref="AggregateRootNotFoundException">
    /// Thrown when no aggregate exists for <paramref name="identifier"/>.
    /// </exception>
    protected abstract IAsyncEnumerable<TEvent> ReadEventsAsync(AggregateIdentifier identifier, CancellationToken cancellationToken = default);
}
