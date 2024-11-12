namespace Aggregates.Projections;

/// <summary>
/// Represents the changes that are made to a projection, but have not been committed yet.
/// </summary>
public interface ICommit {
    /// <summary>
    /// Produces a <see cref="Commit"/> that uses the given <paramref name="applicator"/> to produce the appropriate <see cref="ICommit{TState}"/>.
    /// </summary>
    /// <param name="applicator">A function that produces a <see cref="ICommit{TState}"/>.</param>
    /// <returns>A <see cref="Commit"/>.</returns>
    ICommit Use(Func<ICommit> applicator) => new Commit([this]).Use(applicator);

    /// <summary>
    /// Produces a <see cref="ICommit"/> that adds a deferred commit to the sequence of commits to execute.
    /// </summary>
    /// <typeparam name="TCommit">The type of the returned commit.</typeparam>
    /// <param name="asyncApplicator">A <see cref="Func{TResult}"/> that asynchronously produces the next commit.</param>
    /// <returns></returns>
    ICommit Use<TCommit>(Func<CancellationToken, ValueTask<TCommit>> asyncApplicator) where TCommit : ICommit => new Commit([this]).Use(asyncApplicator);

    /// <summary>
    /// Asynchronously commits the changes made to a projection after applying an event.
    /// </summary>
    /// <param name="cancellationToken">A <see cref="CancellationToken"/> to cancel the asynchronous operation.</param>
    /// <returns>A <see cref="ValueTask"/> that represents the asynchronous operation, which resolves to the new state.</returns>
    ValueTask CommitAsync(CancellationToken cancellationToken = default);
}