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
    ///   <item>
    ///     A <see cref="SagaCommitDelegate"/> backed by <see cref="KurrentDbCommitHandler"/> that
    ///     serializes the saga's changes as <c>$&gt;</c> links to their source events, so a saga
    ///     stream never holds copies of the events it reacts to.
    ///   </item>
    /// </list>
    /// </remarks>
    public static ISagasKurrentDbBuilder AddKurrentDb(this ISagasBuilder builder) {
        // Checkpoint store
        builder.Services.TryAddSingleton<ICheckpointStore, KurrentDbCheckpointStore>();

        // Subscription factory
        builder.Services.TryAddSingleton<ISubscriptionFactory, KurrentDbSubscriptionFactory>();

        // Saga repository
        builder.UseSagaRepository(typeof(KurrentDbSagaRepository<,>));

        // Saga commit delegate — reuses the same KurrentDbCommitHandler as regular aggregates,
        // but serializes the saga's changes as links to their source events instead of copies.
        builder.Services.TryAddSingleton<SagaCommitDelegate>(sp => {
            var handler = sp.GetRequiredService<KurrentDbCommitHandler>();
            return uow => handler.CommitAsync(uow, SerializeLink);
        });

        // Parked-message sink — Replace overrides the LoggingParkedMessageSink fallback registered
        // by AddSagas, regardless of the order in which AddKurrentDb was called.
        builder.Services.Replace(ServiceDescriptor.Singleton<IParkedMessageSink, KurrentDbParkedMessageSink>());

        return new SagasKurrentDbBuilder(builder.Services);
    }

    /// <summary>
    /// Serializes a saga change into the link stored in the saga stream.
    /// </summary>
    /// <exception cref="InvalidOperationException">
    /// Thrown when the change is not an <see cref="EventReference"/>.
    /// </exception>
    static SerializedEvent SerializeLink(object change) =>
        change is EventReference reference
            ? new SerializedEvent(EventReference.LinkEventType, reference.ToLinkData())
            : throw new InvalidOperationException($"A saga's changes are references to source events, but got a {change.GetType().Name}.");
}

sealed class SagasKurrentDbBuilder(IServiceCollection services) : ISagasKurrentDbBuilder {
    public IServiceCollection Services => services;
}
