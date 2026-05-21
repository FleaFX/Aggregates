using Microsoft.Extensions.DependencyInjection;

namespace Aggregates;

/// <summary>
/// A builder for configuring the Aggregates library and its integrations.
/// Storage integration packages (e.g. <c>Aggregates.MSSP</c>) extend this interface
/// with their own extension methods.
/// </summary>
public interface IAggregatesBuilder {
    /// <summary>
    /// The underlying service collection.
    /// </summary>
    IServiceCollection Services { get; }
}
