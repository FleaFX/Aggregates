using System.Collections.Immutable;

namespace Aggregates.Projections;

public class Commit(ImmutableArray<ICommit> commits) : ICommit {
    /// <summary>
    /// Creates a new <see cref="ICommit"/> that doesn't perform any work. Use to configure with calls to <see cref="Use"/>.
    /// </summary>
    /// <returns>A <see cref="ICommit"/>.</returns>
    public static ICommit Create() => new Commit(ImmutableArray<ICommit>.Empty);
    
    /// <summary>
    /// Produces a <see cref="Commit"/> that uses the given <paramref name="applicator"/> to produce the appropriate <see cref="ICommit{TState}"/>.
    /// </summary>
    /// <param name="applicator">A function that produces a <see cref="ICommit{TState}"/>.</param>
    /// <returns>A <see cref="Commit"/>.</returns>
    public ICommit Use(Func<ICommit> applicator) =>
        new Commit(commits.Add(applicator()));

    /// <summary>
    /// Produces a <see cref="Commit"/> that adds a deferred commit to the sequence of commits to execute.
    /// </summary>
    /// <typeparam name="TCommit">The type of the returned commit.</typeparam>
    /// <param name="asyncApplicator">A <see cref="Func{TResult}"/> that asynchronously produces the next commit.</param>
    /// <returns></returns>
    public ICommit Use<TCommit>(Func<CancellationToken, ValueTask<TCommit>> asyncApplicator) where TCommit : ICommit =>
        Use(() => new DeferredCommit<TCommit>(asyncApplicator));

    async ValueTask ICommit.CommitAsync(CancellationToken cancellationToken = default) {
        await CommitAllAsync(commits.ToArray());
        return;

        Task CommitAllAsync(ICommit[] commits) =>
            commits switch {
                [var commit, ..] => Task.Run(async () => {
                    await commit.CommitAsync(cancellationToken);
                    await CommitAllAsync(commits[1..]);
                }, cancellationToken),
                [] => Task.CompletedTask
            };
    }
}