using Microsoft.Extensions.DependencyInjection;

namespace Aggregates.Sagas;

/// <summary>
/// A builder for configuring the <c>Aggregates.Sagas</c> package and its storage integrations.
/// Storage integration packages extend this interface with their own extension methods.
/// </summary>
public interface ISagasBuilder {
    /// <summary>
    /// The underlying service collection.
    /// </summary>
    IServiceCollection Services { get; }
}
