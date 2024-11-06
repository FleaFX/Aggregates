namespace Aggregates.Projections;

/// <summary>
/// Applies the given <paramref name="event"/> to progress a projection to a new state.
/// </summary>
/// <typeparam name="TEvent">The type of the event to apply.</typeparam>
/// <param name="event">The event to apply.</param>
/// <param name="metadata">A set of metadata that was saved with the event, if any.</param>
/// <returns>The <see cref="ICommit"/> that represents the changes needed to progress to the new state.</returns>
public delegate ICommit ProjectionDelegate<in TEvent>(TEvent @event, IReadOnlyDictionary<string, object?>? metadata = null);