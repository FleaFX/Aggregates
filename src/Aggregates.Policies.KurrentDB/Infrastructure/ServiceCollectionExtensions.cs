using Aggregates.KurrentDB;
using Aggregates.Subscriptions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Aggregates.Policies.KurrentDB;

/// <summary>
/// Extension methods for registering <c>Aggregates.Policies.KurrentDB</c> with an
/// <see cref="IServiceCollection"/>.
/// </summary>
public static class ServiceCollectionExtensions {
    /// <summary>
    /// Adds the KurrentDB subscription infrastructure for policies.
    /// Call after <c>AddPolicies</c> on the aggregates builder and <c>AddKurrentDb</c> on the
    /// aggregates builder.
    /// </summary>
    /// <remarks>
    /// Registers:
    /// <list type="bullet">
    ///   <item><see cref="KurrentDbCheckpointStore"/> as <see cref="ICheckpointStore"/> (if not already registered).</item>
    ///   <item><see cref="KurrentDbSubscriptionFactory"/> as <see cref="ISubscriptionFactory"/> (if not already registered).</item>
    /// </list>
    /// </remarks>
    public static IPoliciesKurrentDbBuilder AddKurrentDb(this IPoliciesBuilder builder) {
        // Checkpoint store — shared with sagas if both are used
        builder.Services.TryAddSingleton<ICheckpointStore, KurrentDbCheckpointStore>();

        // Subscription factory — shared with sagas if both are used
        builder.Services.TryAddSingleton<ISubscriptionFactory, KurrentDbSubscriptionFactory>();

        return new PoliciesKurrentDbBuilder(builder.Services);
    }
}

sealed class PoliciesKurrentDbBuilder(IServiceCollection services) : IPoliciesKurrentDbBuilder {
    public IServiceCollection Services => services;
}
