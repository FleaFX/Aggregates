namespace Aggregates.Projections;

/// <summary>
/// Implements the projection logic for a single event type (or marker interface).
/// The implementing class may declare constructor parameters — the DI container resolves them at runtime.
/// </summary>
/// <typeparam name="TEvent">
/// The event type to project. Use a marker interface to handle multiple related event types in a
/// single <see cref="ProjectAsync"/> call, guaranteeing processing order across those event types.
/// </typeparam>
public interface IProjection<in TEvent> {
    /// <summary>
    /// Projects <paramref name="event"/> into an <see cref="ICommit"/> that, when committed,
    /// persists the projection state.
    /// </summary>
    /// <param name="event">The event to project.</param>
    /// <param name="metadata">The metadata stored alongside <paramref name="event"/>.</param>
    /// <param name="cancellationToken">A cancellation token.</param>
    /// <returns>
    /// An <see cref="ICommit"/> representing the pending write(s). Call
    /// <see cref="ICommit.CommitAsync"/> to execute them.
    /// </returns>
    ValueTask<ICommit> ProjectAsync(TEvent @event, EventMetadata metadata, CancellationToken cancellationToken = default);
}
