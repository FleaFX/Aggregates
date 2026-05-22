using Aggregates.MSSP;
using Aggregates.Subscriptions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Aggregates.Policies.MSSP;

public static class ServiceCollectionExtensions {
    /// <summary>
    /// Adds the MSSP subscription infrastructure for policies.
    /// Call after <c>AddPolicies</c> on the aggregates builder and <c>AddMssp</c> on the
    /// aggregates builder.
    /// </summary>
    public static IPoliciesMsspBuilder AddMssp(this IPoliciesBuilder builder) {
        // Checkpoint store — shared with sagas if both are used
        builder.Services.TryAddSingleton<ICheckpointStore, MsspCheckpointStore>();

        // Subscription factory — shared with sagas if both are used
        builder.Services.TryAddSingleton<ISubscriptionFactory, MsspSubscriptionFactory>();

        return new PoliciesMsspBuilder(builder.Services);
    }
}

sealed class PoliciesMsspBuilder(IServiceCollection services) : IPoliciesMsspBuilder {
    /// <inheritdoc />
    public IServiceCollection Services => services;
}
