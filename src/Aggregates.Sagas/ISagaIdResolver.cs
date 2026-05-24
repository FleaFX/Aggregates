namespace Aggregates.Sagas;

/// <summary>
/// Resolves the saga identifier(s) for an incoming event, determining which saga instance(s)
/// the event belongs to.
/// </summary>
/// <typeparam name="TEvent">The type of event to resolve saga identifiers for.</typeparam>
public interface ISagaIdResolver<in TEvent> {
    /// <summary>
    /// Returns the <see cref="AggregateIdentifier"/> of each saga instance that should handle
    /// <paramref name="event"/>. Returns an empty sequence when the event is not relevant to
    /// any saga instance.
    /// </summary>
    /// <param name="event">The incoming event.</param>
    /// <param name="metadata">
    /// The metadata stored alongside <paramref name="event"/>. Useful when saga identifiers
    /// are carried in metadata rather than in the event body itself.
    /// </param>
    IEnumerable<AggregateIdentifier> Resolve(TEvent @event, EventMetadata metadata);
}
