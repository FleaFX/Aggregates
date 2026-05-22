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

    /// <summary>
    /// The saga types registered by <see cref="ServiceCollectionExtensions.AddSagas"/>
    /// via assembly scanning. Each entry provides the state type, event type, and the
    /// concrete saga class, which integration packages use to set up subscriptions.
    /// </summary>
    IReadOnlyList<(Type StateType, Type EventType, Type SagaType)> RegisteredSagas { get; }
}
