namespace Aggregates.Projections;

/// <summary>
/// Handles a single event by invoking the registered <see cref="IProjection{TEvent}"/> and
/// committing the resulting <see cref="ICommit"/>.
/// </summary>
/// <typeparam name="TEvent">The event type this handler processes.</typeparam>
public interface IProjectionHandler<in TEvent> {
    /// <summary>
    /// Handles <paramref name="event"/> by projecting it and committing the result.
    /// </summary>
    /// <param name="event">The event to handle.</param>
    /// <param name="metadata">The metadata stored alongside <paramref name="event"/>.</param>
    /// <param name="cancellationToken">A cancellation token.</param>
    ValueTask HandleAsync(TEvent @event, EventMetadata metadata, CancellationToken cancellationToken = default);
}
