namespace Aggregates.Projections;

/// <summary>
/// Configures the behaviour of a projection.
/// </summary>
/// <param name="name">The name of the projection.</param>
/// <param name="version">The version of the projection. You should only ever have one version for every <paramref name="name"/> in your codebase.</param>
/// <param name="namespace">Optional. A namespace to prepend to the name of your projection.</param>
/// <param name="continueFrom">Optional. Specifies the name of a previous version of the projection to continue projecting from.</param>
[AttributeUsage(AttributeTargets.Class | AttributeTargets.Struct, Inherited = false)]
public class ProjectionContractAttribute(string name, int version = 1, string? @namespace = null, string? continueFrom = null) : Attribute {
    /// <summary>
    /// Returns the fully qualified name of the preceding projection contract, if any.
    /// </summary>
    public string? ContinueFrom => continueFrom ?? (version > 1
        ? new ProjectionContractAttribute(name, version - 1, @namespace).ToString()
        : null);

    /// <summary>
    /// Returns the projection contract name.
    /// </summary>
    /// <returns>A <see cref="string"/>.</returns>
    public override string ToString() => $"{(!string.IsNullOrWhiteSpace(@namespace) ? $"{@namespace}." : string.Empty)}{name}@v{version}";
}