using Microsoft.Extensions.DependencyInjection;

namespace Aggregates.Policies.KurrentDB;

/// <summary>
/// A builder for configuring the <c>Aggregates.Policies.KurrentDB</c> package.
/// </summary>
public interface IPoliciesKurrentDbBuilder {
    /// <summary>
    /// The underlying service collection.
    /// </summary>
    IServiceCollection Services { get; }
}
