namespace Aggregates.Projections;

/// <summary>
/// Defers the creation of the commit until the projection is ready to be committed.
/// </summary>
/// <typeparam name="TCommit">The type of the commit to produce.</typeparam>
/// <param name="AsyncCommitFactory">A function that asynchronously produces the <see cref="ICommit"/> to execute.</param>
sealed record DeferredCommit<TCommit>(Func<CancellationToken, ValueTask<TCommit>> AsyncCommitFactory) : ICommit where TCommit : ICommit {
    /// <summary>
    /// Produces a <see cref="Commit"/> that uses the given <paramref name="applicator"/> to produce the appropriate <see cref="ICommit"/>.
    /// </summary>
    /// <param name="applicator">A function that produces a <see cref="ICommit"/>.</param>
    /// <returns>A <see cref="Commit"/>.</returns>
    public ICommit Use(Func<ICommit> applicator) =>
        new Commit([this]).Use(applicator);

    /// <summary>
    /// Produces a <see cref="ICommit"/> that adds a deferred commit to the sequence of commits to execute.
    /// </summary>
    /// <typeparam name="TNextCommit">The type of the returned commit.</typeparam>
    /// <param name="asyncApplicator">A <see cref="Func{TResult}"/> that asynchronously produces the next commit.</param>
    /// <returns></returns>
    public ICommit Use<TNextCommit>(Func<CancellationToken, ValueTask<TNextCommit>> asyncApplicator) where TNextCommit : ICommit =>
        new Commit([this]).Use(asyncApplicator);

    /// <summary>
    /// Asynchronously commits the changes made to a projection after applying an event.
    /// </summary>
    /// <param name="cancellationToken">A <see cref="CancellationToken"/> to cancel the asynchronous operation.</param>
    /// <returns>A <see cref="ValueTask"/> that represents the asynchronous operation, which resolves to the new state.</returns>
    async ValueTask ICommit.CommitAsync(CancellationToken cancellationToken) =>
        await (await AsyncCommitFactory(cancellationToken)).CommitAsync(cancellationToken);

}