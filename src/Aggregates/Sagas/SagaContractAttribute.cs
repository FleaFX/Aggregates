namespace Aggregates.Sagas;

/// <summary>
/// Configures the behaviour of a saga.
/// </summary>
/// <param name="name">The name of the saga.</param>
/// <param name="version">The version of the saga. You should only ever have one version for every <paramref name="name"/> in your codebase.</param>
/// <param name="namespace">Optional. A namespace to prepend to the name of your saga.</param>
/// <param name="continueFrom">Optional. Specifies the name of a previous version of the saga to continue projecting from.</param>
/// <param name="startFromEnd">Optional. Indicates whether the saga should start from the end of the stream.</param>
[AttributeUsage(AttributeTargets.Class | AttributeTargets.Struct, Inherited = false)]
public class SagaContractAttribute(string name, int version = 1, string? @namespace = null, string? continueFrom = null, bool startFromEnd = false) : Attribute {
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
    /// Returns the saga contract name.
    /// </summary>
    /// <returns>A <see cref="string"/>.</returns>
    public override string ToString() => $"{(!string.IsNullOrWhiteSpace(@namespace) ? $"{@namespace}." : string.Empty)}{name}@v{version}";
}