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
