using Microsoft.Extensions.DependencyInjection;

namespace Aggregates.Sagas.KurrentDB;

/// <summary>
/// A builder for configuring the <c>Aggregates.Sagas.KurrentDB</c> package.
/// </summary>
public interface ISagasKurrentDbBuilder {
    /// <summary>
    /// The underlying service collection.
    /// </summary>
    IServiceCollection Services { get; }
}
