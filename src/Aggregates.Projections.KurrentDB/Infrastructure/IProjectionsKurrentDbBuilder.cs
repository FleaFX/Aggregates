using Microsoft.Extensions.DependencyInjection;

namespace Aggregates.Projections.KurrentDB;

/// <summary>
/// A builder for configuring the <c>Aggregates.Projections.KurrentDB</c> package.
/// </summary>
public interface IProjectionsKurrentDbBuilder {
    /// <summary>
    /// The underlying service collection.
    /// </summary>
    IServiceCollection Services { get; }
}
