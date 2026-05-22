namespace Aggregates.Projections;

/// <summary>
/// Represents a unit of uncommitted projection work that can be composed and committed as a whole.
/// </summary>
/// <remarks>
/// Use <see cref="Commit.Create"/> to start a new chain, then extend it with
/// <see cref="Use(Func{ICommit})"/> or <see cref="Use{TCommit}"/> to append further work.
/// Call <see cref="CommitAsync"/> once at the end to execute everything.
/// </remarks>
public interface ICommit {
    /// <summary>
    /// Appends the result of <paramref name="applicator"/> to this commit chain.
    /// </summary>
    ICommit Use(Func<ICommit> applicator) =>
        new Commit([this, applicator()]);

    /// <summary>
    /// Appends an asynchronously produced commit to this commit chain.
    /// The factory is deferred until <see cref="CommitAsync"/> is called.
    /// </summary>
    ICommit Use<TCommit>(Func<CancellationToken, ValueTask<TCommit>> asyncApplicator)
        where TCommit : ICommit =>
        new Commit([this, new DeferredCommit<TCommit>(asyncApplicator)]);

    /// <summary>
    /// Executes all pending work in this commit chain.
    /// </summary>
    ValueTask CommitAsync(CancellationToken cancellationToken = default);
}
