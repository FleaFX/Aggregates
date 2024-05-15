namespace Aggregates.Metadata;

/// <summary>
/// Indicates whether a metadata pair can contain just a single or multiple values.
/// </summary>
public enum MetadataMultiplicity {
    /// <summary>
    /// Indicates that a metadata key can only ever contain a single value. Should a key be specified multiple times, then any existing value will be overwritten.
    /// </summary>
    Single,

    /// <summary>
    /// Indicates that a metadata key can contain multiple values. Values will be represented as an array.
    /// </summary>
    Multiple
}