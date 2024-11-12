namespace Aggregates.Policies;

/// <summary>
/// Configures the behaviour of a policy.
/// </summary>
/// <param name="name">The name of the policy.</param>
/// <param name="version">The version of the policy. You should only ever have one version for every <paramref name="name"/> in your codebase.</param>
/// <param name="namespace">Optional. A namespace to prepend to the name of your policy.</param>
/// <param name="continueFrom">Optional. Specifies the name of a previous version of the policy to continue projecting from.</param>
[AttributeUsage(AttributeTargets.Class | AttributeTargets.Struct, Inherited = false)]
public class PolicyContractAttribute(string name, int version = 1, string? @namespace = null, string? continueFrom = null) : Attribute {
    /// <summary>
    /// Returns the fully qualified name of the preceding policy contract, if any.
    /// </summary>
    public string? ContinueFrom => continueFrom ?? (version > 1
        ? new PolicyContractAttribute(name, version - 1, @namespace).ToString()
        : null);

    /// <summary>
    /// Returns the policy contract name.
    /// </summary>
    /// <returns>A <see cref="string"/>.</returns>
    public override string ToString() => $"{(!string.IsNullOrWhiteSpace(@namespace) ? $"{@namespace}." : string.Empty)}{name}@v{version}";
}