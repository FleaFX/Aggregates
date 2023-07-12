namespace Aggregates;

interface IAggregateRoot {
    /// <summary>
    /// Gets the version that the aggregate instance is at.
    /// </summary>
    AggregateVersion Version { get; }

    /// <summary>
    /// Gets the sequence of changes that were applied, if any.
    /// </summary>
    /// <returns>A <see cref="IEnumerable{T}"/>.</returns>
    IEnumerable<object> GetChanges();
}