using Microsoft.Extensions.DependencyInjection;

namespace Aggregates.KurrentDB;

/// <summary>
/// A builder for configuring the <c>Aggregates.KurrentDB</c> package and its extensions.
/// </summary>
public interface IKurrentDbBuilder {
    /// <summary>
    /// The underlying service collection.
    /// </summary>
    IServiceCollection Services { get; }
}
