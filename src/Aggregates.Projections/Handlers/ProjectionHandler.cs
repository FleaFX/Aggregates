namespace Aggregates.Projections;

/// <summary>
/// Abstract base for the projection handler decorator chain.
/// </summary>
abstract class ProjectionHandler<TEvent> : IProjectionHandler<TEvent> {
    /// <inheritdoc/>
    public abstract ValueTask HandleAsync(TEvent @event, EventMetadata metadata, CancellationToken cancellationToken = default);
}

/// <summary>
/// Invokes <typeparamref name="TProjection"/>'s <see cref="IProjection{TEvent}.ProjectAsync"/> and
/// commits the resulting <see cref="ICommit"/>.
/// </summary>
sealed class ProjectionHandler<TProjection, TEvent>(IProjection<TEvent> projection)
    : ProjectionHandler<TEvent>
    where TProjection : IProjection<TEvent> {

    /// <inheritdoc/>
    public override async ValueTask HandleAsync(TEvent @event, EventMetadata metadata, CancellationToken cancellationToken = default) {
        var commit = await projection.ProjectAsync(@event, metadata, cancellationToken);
        await commit.CommitAsync(cancellationToken);
    }
}
