namespace Aggregates.Projections;

/// <summary>
/// Invokes <typeparamref name="TProjection"/>'s <see cref="IProjection{TEvent}.ProjectAsync"/> and
/// commits the resulting <see cref="ICommit"/>. The innermost link of the projection handler chain.
/// </summary>
/// <typeparam name="TProjection">The projection class.</typeparam>
/// <typeparam name="TEvent">The event type the projection handles.</typeparam>
sealed class ProjectionHandler<TProjection, TEvent>(TProjection projection)
    where TProjection : IProjection<TEvent> {

    /// <summary>
    /// Projects <paramref name="event"/> and commits the result.
    /// </summary>
    /// <param name="event">The event to project.</param>
    /// <param name="metadata">The metadata stored alongside <paramref name="event"/>.</param>
    /// <param name="cancellationToken">A cancellation token.</param>
    public async ValueTask HandleAsync(TEvent @event, EventMetadata metadata, CancellationToken cancellationToken = default) {
        var commit = await projection.ProjectAsync(@event, metadata, cancellationToken);
        await commit.CommitAsync(cancellationToken);
    }
}
