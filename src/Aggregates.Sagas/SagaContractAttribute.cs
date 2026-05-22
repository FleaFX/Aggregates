namespace Aggregates.Sagas;

/// <summary>
/// Configures the identity and behaviour of a saga.
/// </summary>
/// <param name="name">The name of the saga.</param>
/// <param name="version">The version of the saga. Defaults to 1.</param>
/// <param name="namespace">An optional namespace prepended to the saga name.</param>
/// <param name="continueFrom">
/// The fully qualified name of a preceding saga version to continue from.
/// When omitted and <paramref name="version"/> is greater than 1, defaults to the previous version.
/// </param>
/// <param name="startFromEnd">
/// When <see langword="true"/>, the saga starts processing from the end of the stream
/// rather than the beginning.
/// </param>
[AttributeUsage(AttributeTargets.Class | AttributeTargets.Struct, Inherited = false)]
public sealed class SagaContractAttribute(
    string name,
    int version = 1,
    string? @namespace = null,
    string? continueFrom = null,
    bool startFromEnd = false) : Attribute {

    /// <summary>
    /// Returns the fully qualified name of the preceding saga contract, if any.
    /// </summary>
    public string? ContinueFrom => continueFrom ?? (version > 1
        ? new SagaContractAttribute(name, version - 1, @namespace).ToString()
        : null);

    /// <summary>
    /// Indicates whether the saga should start from the end of the stream.
    /// </summary>
    public bool StartFromEnd => startFromEnd;

    /// <summary>
    /// Returns the fully qualified saga name, e.g. <c>MyNamespace.OrderFulfillment@v2</c>.
    /// </summary>
    public override string ToString() =>
        $"{(!string.IsNullOrWhiteSpace(@namespace) ? $"{@namespace}." : string.Empty)}{name}@v{version}";
}
