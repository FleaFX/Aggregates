namespace Aggregates.Projections;

/// <summary>
/// Represents the changes that are made to a projection, but have not been committed yet.
/// </summary>
public interface ICommit {
    /// <summary>
    /// Asynchronously commits the changes made to a projection after applying an event.
    /// </summary>
    /// <param name="cancellationToken">A <see cref="CancellationToken"/> to cancel the asynchronous operation.</param>
    /// <returns>A <see cref="ValueTask"/> that represents the asynchronous operation, which resolves to the new state.</returns>
    ValueTask CommitAsync(CancellationToken cancellationToken = default);
}

/// <summary>
/// Represents the changes that are made to a projection, but have not been committed yet.
/// </summary>
/// <typeparam name="TState">The type of the state that is produced after committing the changes.</typeparam>
public interface ICommit<TState> : ICommit {
    /// <summary>
    /// Asynchronously commits the changes made to a projection after applying an event.
    /// </summary>
    /// <param name="cancellationToken">A <see cref="CancellationToken"/> to cancel the asynchronous operation.</param>
    /// <returns>A <see cref="ValueTask"/> that represents the asynchronous operation, which resolves to the new state.</returns>
    new ValueTask<TState> CommitAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Asynchronously commits the changes made to a projection after applying an event.
    /// </summary>
    /// <param name="cancellationToken">A <see cref="CancellationToken"/> to cancel the asynchronous operation.</param>
    /// <returns>A <see cref="ValueTask"/> that represents the asynchronous operation, which resolves to the new state.</returns>
    async ValueTask ICommit.CommitAsync(CancellationToken cancellationToken) => await ((ICommit<TState>)this).CommitAsync(cancellationToken);
}