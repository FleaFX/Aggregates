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
    ValueTask HandleAsync(TEvent @event, CancellationToken cancellationToken = default);
}
