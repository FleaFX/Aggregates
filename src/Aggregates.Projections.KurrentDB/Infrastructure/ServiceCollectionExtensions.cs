using Aggregates.KurrentDB;
using Aggregates.Subscriptions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Aggregates.Projections.KurrentDB;

/// <summary>
/// Extension methods for registering <c>Aggregates.Projections.KurrentDB</c> with an
/// <see cref="IServiceCollection"/>.
/// </summary>
public static class ServiceCollectionExtensions {
    /// <summary>
    /// Adds the KurrentDB subscription infrastructure for projections.
    /// Call after <c>AddProjections</c> and <c>AddKurrentDb</c> on the aggregates builder.
    /// </summary>
    /// <remarks>
    /// Registers:
    /// <list type="bullet">
    ///   <item><see cref="KurrentDbCheckpointStore"/> as <see cref="ICheckpointStore"/> (if not already registered).</item>
    ///   <item><see cref="KurrentDbSubscriptionFactory"/> as <see cref="ISubscriptionFactory"/> (if not already registered).</item>
    /// </list>
    /// </remarks>
    public static IProjectionsKurrentDbBuilder AddKurrentDb(this IProjectionsBuilder builder) {
        // Checkpoint store — shared with sagas/policies if both are used
        builder.Services.TryAddSingleton<ICheckpointStore, KurrentDbCheckpointStore>();

        // Subscription factory — shared with sagas/policies if both are used
        builder.Services.TryAddSingleton<ISubscriptionFactory, KurrentDbSubscriptionFactory>();

        // Parked-message sink — Replace overrides the LoggingParkedMessageSink fallback registered
        // by AddProjections, regardless of the order in which AddKurrentDb was called.
        builder.Services.Replace(ServiceDescriptor.Singleton<IParkedMessageSink, KurrentDbParkedMessageSink>());

        return new ProjectionsKurrentDbBuilder(builder.Services);
    }
}

sealed class ProjectionsKurrentDbBuilder(IServiceCollection services) : IProjectionsKurrentDbBuilder {
    public IServiceCollection Services => services;
}
