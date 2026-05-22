using Microsoft.Extensions.DependencyInjection;

namespace Aggregates.MSSP;

/// <summary>
/// A builder for configuring the <c>Aggregates.MSSP</c> package and its extensions.
/// </summary>
public interface IMsspBuilder {
    /// <summary>
    /// The underlying service collection.
    /// </summary>
    IServiceCollection Services { get; }
}
