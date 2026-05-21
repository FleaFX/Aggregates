namespace Aggregates;

/// <summary>
/// Thrown when writing an aggregate fails because its current version in the store does not match
/// the expected version, indicating a concurrent modification.
/// </summary>
public class ConcurrencyException(
    AggregateIdentifier identifier,
    AggregateVersion expectedVersion,
    AggregateVersion actualVersion
) : Exception($"Concurrency conflict on '{identifier.Value}': expected version {(long)expectedVersion}, actual {(long)actualVersion}.") {

    /// <summary>
    /// The identifier of the aggregate that caused the conflict.
    /// </summary>
    public AggregateIdentifier Identifier { get; } = identifier;

    /// <summary>
    /// The version the caller expected the aggregate to be at.
    /// </summary>
    public AggregateVersion ExpectedVersion { get; } = expectedVersion;

    /// <summary>
    /// The version the aggregate was actually at in the store.
    /// </summary>
    public AggregateVersion ActualVersion { get; } = actualVersion;
}
