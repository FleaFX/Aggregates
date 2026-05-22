namespace Aggregates.Projections;

/// <summary>
/// Configures the behaviour and identity of a projection.
/// </summary>
[AttributeUsage(AttributeTargets.Class | AttributeTargets.Struct, Inherited = false)]
public sealed class ProjectionContractAttribute(
    string name,
    int version = 1,
    string? @namespace = null,
    bool startFromEnd = false) : Attribute {

    /// <summary>
    /// Indicates whether the projection should start from the end of the stream on first run.
    /// </summary>
    public bool StartFromEnd => startFromEnd;

    /// <inheritdoc/>
    public override string ToString() =>
        $"{(!string.IsNullOrWhiteSpace(@namespace) ? $"{@namespace}." : string.Empty)}{name}@v{version}";
}
