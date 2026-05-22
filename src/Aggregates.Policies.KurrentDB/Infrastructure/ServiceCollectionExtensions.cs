using System.Reflection;
using Aggregates.KurrentDB;
using Aggregates.Subscriptions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;

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
    ///   <item>A <see cref="PolicySubscriptionService{TEvent}"/> hosted service for every registered policy.</item>
    /// </list>
    /// </remarks>
    public static IPoliciesKurrentDbBuilder AddKurrentDb(this IPoliciesBuilder builder) {
        // Checkpoint store — shared with sagas if both are used
        builder.Services.TryAddSingleton<ICheckpointStore, KurrentDbCheckpointStore>();

        // Subscription hosted service — one per policy type
        foreach (var (eventType, policyType) in builder.RegisteredPolicies) {
            var subscriptionId = GetSubscriptionId(policyType);
            var startFromEnd = GetStartFromEnd(policyType);
            var serviceType = typeof(PolicySubscriptionService<>).MakeGenericType(eventType);

            builder.Services.AddSingleton(typeof(IHostedService), sp =>
                ActivatorUtilities.CreateInstance(sp, serviceType, subscriptionId, startFromEnd));
        }

        return new PoliciesKurrentDbBuilder(builder.Services);
    }

    static string GetSubscriptionId(Type policyType) {
        var attr = policyType.GetCustomAttribute<PolicyContractAttribute>();
        return attr?.ToString() ?? policyType.FullName ?? policyType.Name;
    }

    static bool GetStartFromEnd(Type policyType) =>
        policyType.GetCustomAttribute<PolicyContractAttribute>()?.StartFromEnd ?? false;
}

internal sealed class PoliciesKurrentDbBuilder(IServiceCollection services) : IPoliciesKurrentDbBuilder {
    public IServiceCollection Services => services;
}
