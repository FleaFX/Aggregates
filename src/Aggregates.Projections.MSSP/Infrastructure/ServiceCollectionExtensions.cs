using Aggregates.MSSP;
using Aggregates.Subscriptions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Aggregates.Projections.MSSP;

public static class ServiceCollectionExtensions {
    /// <summary>
    /// Adds the MSSP subscription infrastructure for projections.
    /// Call after <c>AddProjections</c> and <c>AddMssp</c> on the aggregates builder.
    /// </summary>
    public static IProjectionsMsspBuilder AddMssp(this IProjectionsBuilder builder) {
        // Checkpoint store — shared with sagas/policies if both are used
        builder.Services.TryAddSingleton<ICheckpointStore, MsspCheckpointStore>();

        // Subscription factory — shared with sagas/policies if both are used
        builder.Services.TryAddSingleton<ISubscriptionFactory, MsspSubscriptionFactory>();

        return new ProjectionsMsspBuilder(builder.Services);
    }
}

sealed class ProjectionsMsspBuilder(IServiceCollection services) : IProjectionsMsspBuilder {
    /// <inheritdoc />
    public IServiceCollection Services => services;
}
