using System.Collections.Immutable;

namespace Aggregates.Projections;

/// <summary>
/// An <see cref="ICommit"/> that composes a sequence of child commits and executes them in order.
/// </summary>
public sealed class Commit : ICommit {
    readonly ImmutableArray<ICommit> _commits;

    internal Commit(ImmutableArray<ICommit> commits) => _commits = commits;

    /// <summary>
    /// Creates an empty commit chain. Use <see cref="ICommit.Use(Func{ICommit})"/> to append work.
    /// </summary>
    public static ICommit Create() => new Commit(ImmutableArray<ICommit>.Empty);

    /// <inheritdoc/>
    public ICommit Use(Func<ICommit> applicator) =>
        new Commit(_commits.Add(applicator()));

    /// <inheritdoc/>
    public ICommit Use<TCommit>(Func<CancellationToken, ValueTask<TCommit>> asyncApplicator)
        where TCommit : ICommit =>
        new Commit(_commits.Add(new DeferredCommit<TCommit>(asyncApplicator)));

    /// <inheritdoc/>
    public async ValueTask CommitAsync(CancellationToken cancellationToken = default) {
        foreach (var commit in _commits)
            await commit.CommitAsync(cancellationToken);
    }
}
