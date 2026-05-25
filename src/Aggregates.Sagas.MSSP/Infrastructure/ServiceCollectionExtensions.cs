using Aggregates.MSSP;
using Aggregates.Subscriptions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Aggregates.Sagas.MSSP;

/// <summary>
/// Extension methods for registering <c>Aggregates.Sagas.MSSP</c> with an
/// <see cref="IServiceCollection"/>.
/// </summary>
public static class ServiceCollectionExtensions {
    /// <summary>
    /// Adds the MSSP storage and subscription infrastructure for sagas.
    /// Call after <c>AddSagas</c> on the aggregates builder and <c>AddMssp</c> on the
    /// aggregates builder.
    /// </summary>
    public static ISagasMsspBuilder AddMssp(this ISagasBuilder builder) {
        // Checkpoint store
        builder.Services.TryAddSingleton<ICheckpointStore, MsspCheckpointStore>();

        // Subscription factory
        builder.Services.TryAddSingleton<ISubscriptionFactory, MsspSubscriptionFactory>();

        // Saga repository
        builder.UseSagaRepository(typeof(MsspSagaRepository<,>));

        // Saga commit delegate — reuses the same MsspCommitHandler as regular aggregates
        builder.Services.TryAddSingleton<SagaCommitDelegate>(
            sp => sp.GetRequiredService<MsspCommitHandler>().CommitAsync);

        // Parked-message sink — Replace overrides the LoggingParkedMessageSink fallback registered
        // by AddSagas, regardless of the order in which AddMssp was called.
        builder.Services.Replace(ServiceDescriptor.Singleton<IParkedMessageSink, MsspParkedMessageSink>());

        return new SagasMsspBuilder(builder.Services);
    }
}

sealed class SagasMsspBuilder(IServiceCollection services) : ISagasMsspBuilder {
    public IServiceCollection Services => services;
}
