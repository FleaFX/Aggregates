using Microsoft.Extensions.DependencyInjection;

namespace Aggregates.Policies;

/// <summary>
/// A builder for configuring the <c>Aggregates.Policies</c> package and its storage integrations.
/// </summary>
public interface IPoliciesBuilder {
    /// <summary>
    /// The underlying service collection.
    /// </summary>
    IServiceCollection Services { get; }

    /// <summary>
    /// The policies registered via <see cref="PoliciesOptions.ScanAssemblies"/>, as
    /// <c>(EventType, PolicyType)</c> pairs. Used by integration packages to set up
    /// subscription services.
    /// </summary>
    IReadOnlyList<(Type EventType, Type PolicyType)> RegisteredPolicies { get; }
}
