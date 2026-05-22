using Aggregates.KurrentDB;
using Aggregates.Subscriptions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Aggregates.Sagas.KurrentDB;

/// <summary>
/// Extension methods for registering <c>Aggregates.Sagas.KurrentDB</c> with an
/// <see cref="IServiceCollection"/>.
/// </summary>
public static class ServiceCollectionExtensions {
    /// <summary>
    /// Adds the KurrentDB storage and subscription infrastructure for sagas.
    /// Call after <c>AddSagas</c> on the aggregates builder and <c>AddKurrentDb</c> on the
    /// aggregates builder.
    /// </summary>
    /// <remarks>
    /// Registers:
    /// <list type="bullet">
    ///   <item><see cref="KurrentDbCheckpointStore"/> as <see cref="ICheckpointStore"/>.</item>
    ///   <item><see cref="KurrentDbSubscriptionFactory"/> as <see cref="ISubscriptionFactory"/>.</item>
    ///   <item><see cref="KurrentDbSagaRepository{TSagaState,TEvent}"/> as the saga repository.</item>
    ///   <item>A <see cref="SagaCommitDelegate"/> backed by <see cref="KurrentDbCommitHandler"/>.</item>
    /// </list>
    /// </remarks>
    public static ISagasKurrentDbBuilder AddKurrentDb(this ISagasBuilder builder) {
        // Checkpoint store
        builder.Services.TryAddSingleton<ICheckpointStore, KurrentDbCheckpointStore>();

        // Subscription factory
        builder.Services.TryAddSingleton<ISubscriptionFactory, KurrentDbSubscriptionFactory>();

        // Saga repository
        builder.UseSagaRepository(typeof(KurrentDbSagaRepository<,>));

        // Saga commit delegate — reuses the same KurrentDbCommitHandler as regular aggregates
        builder.Services.TryAddSingleton<SagaCommitDelegate>(
            sp => sp.GetRequiredService<KurrentDbCommitHandler>().CommitAsync);

        return new SagasKurrentDbBuilder(builder.Services);
    }
}

sealed class SagasKurrentDbBuilder(IServiceCollection services) : ISagasKurrentDbBuilder {
    public IServiceCollection Services => services;
}
