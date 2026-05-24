namespace Aggregates;

/// <summary>
/// Indicates whether a metadata key can hold a single value or accumulate multiple values.
/// </summary>
public enum MetadataMultiplicity {
    /// <summary>
    /// The key holds exactly one value. Adding a new value for the same key overwrites the existing one.
    /// </summary>
    Single,

    /// <summary>
    /// The key accumulates values. Adding a new value for the same key appends it to the collection.
    /// Duplicate values are silently ignored. The accumulated values are exposed as an array in
    /// the resulting <see cref="EventMetadata"/>.
    /// </summary>
    Multiple
}
