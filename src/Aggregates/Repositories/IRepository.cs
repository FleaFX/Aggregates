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
    /// Returns the root tracked by the ambient <see cref="UnitOfWork"/> for <paramref name="identifier"/>, or reads and attaches it;
    /// <see langword="null"/> when the aggregate doesn't exist.
    /// </summary>
    /// <param name="identifier">The identifier of the aggregate to retrieve.</param>
    /// <param name="cancellationToken">A token to cancel the operation.</param>
    ValueTask<EntityRoot<TState, TEvent>?> TryGetEntityRootAsync(AggregateIdentifier identifier, CancellationToken cancellationToken = default);

    /// <summary>
    /// Attaches a new <paramref name="entityRoot"/> under <paramref name="identifier"/> to the ambient <see cref="UnitOfWork"/>, which commits its changes.
    /// </summary>
    /// <param name="identifier">The identifier to attach the root under.</param>
    /// <param name="entityRoot">The root of the new aggregate.</param>
    void Add(AggregateIdentifier identifier, EntityRoot<TState, TEvent> entityRoot);
}
