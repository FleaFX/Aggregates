using System.Reflection;
using Aggregates.KurrentDB;
using Aggregates.Subscriptions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;

namespace Aggregates.Sagas.KurrentDB;

/// <summary>
/// Extension methods for registering <c>Aggregates.Sagas.KurrentDB</c> with an
/// <see cref="IServiceCollection"/>.
/// </summary>
public static class ServiceCollectionExtensions {
    /// <summary>
    /// Adds the KurrentDB storage and subscription infrastructure for sagas.
    /// Call after <c>AddSagas</c> on the aggregates builder and <c>AddKurrentDb</c> on the sagas builder.
    /// </summary>
    /// <remarks>
    /// Registers:
    /// <list type="bullet">
    ///   <item><see cref="KurrentDbCheckpointStore"/> as <see cref="ICheckpointStore"/>.</item>
    ///   <item><see cref="KurrentDbSagaRepository{TSagaState,TEvent}"/> as the saga repository.</item>
    ///   <item>A <see cref="SagaCommitDelegate"/> backed by <see cref="KurrentDbCommitHandler"/>.</item>
    ///   <item>A <see cref="SagaSubscriptionService{TSagaState,TEvent}"/> hosted service for every
    ///         registered saga that has a corresponding <see cref="ISagaIdResolver{TEvent}"/>.</item>
    /// </list>
    /// </remarks>
    public static ISagasKurrentDbBuilder AddKurrentDb(this ISagasBuilder builder) {
        // Checkpoint store
        builder.Services.TryAddSingleton<ICheckpointStore, KurrentDbCheckpointStore>();

        // Saga repository
        builder.UseSagaRepository(typeof(KurrentDbSagaRepository<,>));

        // Saga commit delegate — reuses the same KurrentDbCommitHandler as regular aggregates
        builder.Services.TryAddSingleton<SagaCommitDelegate>(
            sp => sp.GetRequiredService<KurrentDbCommitHandler>().CommitAsync);

        // Subscription hosted service — one per saga type that has a registered resolver
        foreach (var (stateType, eventType, sagaType) in builder.RegisteredSagas) {
            var resolverType = typeof(ISagaIdResolver<>).MakeGenericType(eventType);
            if (!builder.Services.Any(sd => sd.ServiceType == resolverType))
                continue;

            var subscriptionId = GetSubscriptionId(sagaType);
            var startFromEnd = GetStartFromEnd(sagaType);
            var serviceType = typeof(SagaSubscriptionService<,>).MakeGenericType(stateType, eventType);

            builder.Services.AddSingleton(typeof(IHostedService), sp =>
                ActivatorUtilities.CreateInstance(sp, serviceType, subscriptionId, startFromEnd));
        }

        return new SagasKurrentDbBuilder(builder.Services);
    }

    static string GetSubscriptionId(Type sagaType) {
        var attr = sagaType.GetCustomAttribute<SagaContractAttribute>();
        return attr?.ToString() ?? sagaType.FullName ?? sagaType.Name;
    }

    static bool GetStartFromEnd(Type sagaType) =>
        sagaType.GetCustomAttribute<SagaContractAttribute>()?.StartFromEnd ?? false;
}

internal sealed class SagasKurrentDbBuilder(IServiceCollection services) : ISagasKurrentDbBuilder {
    public IServiceCollection Services => services;
}
