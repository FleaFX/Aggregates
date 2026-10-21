using Microsoft.Extensions.DependencyInjection;

namespace Aggregates.Projections;

/// <summary>
/// A builder for configuring the <c>Aggregates.Projections</c> package and its storage integrations.
/// </summary>
public interface IProjectionsBuilder {
    /// <summary>
    /// The underlying service collection.
    /// </summary>
    IServiceCollection Services { get; }
}
