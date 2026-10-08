using System.Reflection;
using Aggregates.Subscriptions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;

namespace Aggregates.Policies;

/// <summary>
/// Extension methods for registering <c>Aggregates.Policies</c> with an
/// <see cref="Microsoft.Extensions.DependencyInjection.IServiceCollection"/>.
/// </summary>
public static class ServiceCollectionExtensions {
    /// <summary>
    /// Adds the <c>Aggregates.Policies</c> package to the service collection.
    /// Call after <see cref="Aggregates.ServiceCollectionExtensions.AddAggregates"/>.
    /// </summary>
    /// <param name="builder">The aggregates builder returned by <c>AddAggregates</c>.</param>
    /// <param name="configure">
    /// Optional configuration callback. Use <see cref="PoliciesOptions.ScanAssemblies"/> to
    /// automatically register a handler for every <see cref="IPolicy{TEvent}"/> implementation
    /// found in those assemblies.
    /// Use <see cref="PoliciesOptions.ScanTypes"/> instead to register an explicit set of types.
    /// </param>
    /// <remarks>
    /// Every policy class gets one subscription, with the string form of its
    /// <see cref="PolicyContractAttribute"/> as subscription id (or its full type name when it has none).
    /// </remarks>
    /// <exception cref="InvalidOperationException">
    /// A class implements <see cref="IPolicy{TEvent}"/> more than once, or two classes share a
    /// subscription id.
    /// </exception>
    public static IPoliciesBuilder AddPolicies(this IAggregatesBuilder builder, Action<PoliciesOptions>? configure = null) {
        var options = new PoliciesOptions();
        configure?.Invoke(options);

        // Handler chain per policy class: LoggingPolicyHandler<,> → PolicyHandler<,> → TPolicy
        builder.Services.TryAddScoped(typeof(LoggingPolicyHandler<,>));
        builder.Services.TryAddScoped(typeof(PolicyHandler<,>));

        // Subscription error handling — transport packages register their own IParkedMessageSink;
        // LoggingParkedMessageSink is the fallback for dev/test scenarios without a transport.
        builder.Services.TryAddSingleton<IParkedMessageSink, LoggingParkedMessageSink>();
        builder.Services.TryAddSingleton(new SubscriptionErrorHandlingOptions());
        builder.Services.TryAddSingleton<SubscriptionRetryPolicy>();

        // Subscription checkpointing — positions are written in batches
        builder.Services.TryAddSingleton(new SubscriptionCheckpointOptions());
        builder.Services.TryAddSingleton(TimeProvider.System);

        // Subscription loop — subscribes again after a transient failure
        builder.Services.TryAddSingleton(new SubscriptionResubscribeOptions());
        builder.Services.TryAddSingleton<SubscriptionLoop>();

        var registeredPolicies = new List<(Type EventType, Type PolicyType)>();

        foreach (var (policyType, policyInterface) in SubscriptionRegistrations.FindHandlers(options.Types, typeof(IPolicy<>))) {
            var eventType = policyInterface.GetGenericArguments()[0];
            var contract = policyType.GetCustomAttribute<PolicyContractAttribute>();
            var registration = new SubscriptionRegistration(
                SubscriptionRegistrations.GetSubscriptionId(policyType, contract),
                policyType,
                eventType,
                contract?.StartFromEnd ?? false);

            if (!SubscriptionRegistrations.Add(builder.Services, registration))
                continue;

            builder.Services.TryAddScoped(policyType);

            // Subscription hosted service — one per policy class
            var serviceType = typeof(PolicySubscriptionService<,>).MakeGenericType(policyType, eventType);
            builder.Services.AddSingleton(typeof(IHostedService), sp => ActivatorUtilities.CreateInstance(sp, serviceType, registration));

            registeredPolicies.Add((eventType, policyType));
        }

        return new PoliciesBuilder(builder.Services, registeredPolicies);
    }
}

internal sealed class PoliciesBuilder(
    IServiceCollection services,
    IReadOnlyList<(Type EventType, Type PolicyType)> registeredPolicies) : IPoliciesBuilder {
    public IServiceCollection Services => services;
    public IReadOnlyList<(Type EventType, Type PolicyType)> RegisteredPolicies => registeredPolicies;
}
