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
}
