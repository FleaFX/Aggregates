using Aggregates.Projections;

namespace Aggregates;

/// <summary>
/// Marker interface for projections, which maintain a state using events sourced from multiple streams.
/// </summary>
[Obsolete("Use the new ProjectionContract attribute instead.", false)]
public interface IProjection<TState, in TEvent> {
    /// <summary>
    /// Applies the given <paramref name="event"/> to progress to a new state.
    /// </summary>
    /// <param name="event">The event to apply.</param>
    /// <param name="metadata">A set of metadata that was saved with the event, if any.</param>
    /// <returns>The new state.</returns>
    ICommit<TState> Apply(TEvent @event, IReadOnlyDictionary<string, object?>? metadata = null);
}