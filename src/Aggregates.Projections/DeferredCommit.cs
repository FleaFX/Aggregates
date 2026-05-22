namespace Aggregates.Projections;

/// <summary>
/// An <see cref="ICommit"/> whose actual commit is produced asynchronously at commit time.
/// Used internally by <see cref="ICommit.Use{TCommit}"/>.
/// </summary>
sealed class DeferredCommit<TCommit>(Func<CancellationToken, ValueTask<TCommit>> factory) : ICommit
    where TCommit : ICommit {

    /// <inheritdoc/>
    public async ValueTask CommitAsync(CancellationToken cancellationToken = default) {
        var commit = await factory(cancellationToken);
        await commit.CommitAsync(cancellationToken);
    }
}
