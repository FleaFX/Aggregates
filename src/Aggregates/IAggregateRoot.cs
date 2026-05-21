namespace Aggregates;

/// <summary>
/// Represents the root of an aggregate, tracking its version and pending changes.
/// </summary>
public interface IAggregateRoot {
    /// <summary>
    /// Gets the version of the aggregate at the time it was loaded.
    /// </summary>
    AggregateVersion Version { get; }

    /// <summary>
    /// Gets the events that were applied since the aggregate was loaded.
    /// </summary>
    IEnumerable<object> GetChanges();
}
