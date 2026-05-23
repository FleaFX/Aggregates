using Microsoft.Extensions.DependencyInjection;

namespace Aggregates.Projections.MSSP;

/// <summary>
/// A builder for configuring the <c>Aggregates.Projections.MSSP</c> package.
/// </summary>
public interface IProjectionsMsspBuilder {
    /// <summary>
    /// The underlying service collection.
    /// </summary>
    IServiceCollection Services { get; }
}
