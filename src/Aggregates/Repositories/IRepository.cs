namespace Aggregates;

/// <summary>
/// Provides access to aggregates of <typeparamref name="TState"/>.
/// </summary>
/// <typeparam name="TState">The type of the state object.</typeparam>
/// <typeparam name="TEvent">The type of the events applicable to this aggregate.</typeparam>
public interface IRepository<TState, TEvent> where TState : IState<TState, TEvent> {
    /// <summary>
    /// Retrieves the current state of the aggregate identified by <paramref name="identifier"/>.
    /// </summary>
    /// <param name="identifier">The identifier of the aggregate to retrieve.</param>
    /// <param name="cancellationToken">A token to cancel the operation.</param>
    /// <returns>The current state of the aggregate.</returns>
    /// <exception cref="AggregateRootNotFoundException">Thrown when no aggregate exists for <paramref name="identifier"/>.</exception>
    async ValueTask<TState> GetAsync(AggregateIdentifier identifier, CancellationToken cancellationToken = default) {
        var root = await TryGetEntityRootAsync(identifier, cancellationToken);
        if (root is null) throw new AggregateRootNotFoundException(identifier);
        return root.State;
    }

    /// <summary>
    /// Attempts to retrieve the <see cref="EntityRoot{TState,TEvent}"/> for <paramref name="identifier"/>.
    /// Returns <see langword="null"/> when not found.
    /// </summary>
    internal ValueTask<EntityRoot<TState, TEvent>?> TryGetEntityRootAsync(AggregateIdentifier identifier, CancellationToken cancellationToken = default);

    /// <summary>
    /// Attaches a new <paramref name="entityRoot"/> to the repository under <paramref name="identifier"/>.
    /// </summary>
    internal void Add(AggregateIdentifier identifier, EntityRoot<TState, TEvent> entityRoot);
}
