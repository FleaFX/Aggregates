using Microsoft.Extensions.DependencyInjection;

namespace Aggregates.Policies.MSSP;

/// <summary>
/// A builder for configuring the <c>Aggregates.Policies.MSSP</c> package.
/// </summary>
public interface IPoliciesMsspBuilder {
    /// <summary>
    /// The underlying service collection.
    /// </summary>
    IServiceCollection Services { get; }
}
