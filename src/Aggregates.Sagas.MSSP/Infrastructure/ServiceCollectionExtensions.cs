using Aggregates.MSSP;
using Aggregates.Subscriptions;
using MSSP;
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
    /// <remarks>
    /// Registers:
    /// <list type="bullet">
    ///   <item><see cref="MsspCheckpointStore"/> as <see cref="ICheckpointStore"/>.</item>
    ///   <item><see cref="MsspSubscriptionFactory"/> as <see cref="ISubscriptionFactory"/>.</item>
    ///   <item><see cref="MsspSagaRepository{TSagaState,TEvent}"/> as the saga repository.</item>
    ///   <item>
    ///     A <see cref="SagaCommitDelegate"/> backed by <see cref="MsspCommitHandler"/> that
    ///     serializes the saga's changes as <c>$&gt;</c> links to their source events, so a saga
    ///     stream never holds copies of the events it reacts to.
    ///   </item>
    /// </list>
    /// </remarks>
    public static ISagasMsspBuilder AddMssp(this ISagasBuilder builder) {
        // Checkpoint store
        builder.Services.TryAddSingleton<ICheckpointStore, MsspCheckpointStore>();

        // Subscription factory
        builder.Services.TryAddSingleton<ISubscriptionFactory, MsspSubscriptionFactory>();

        // Saga repository
        builder.UseSagaRepository(typeof(MsspSagaRepository<,>));

        // Saga commit delegate — reuses the same MsspCommitHandler as regular aggregates,
        // but serializes the saga's changes as links to their source events instead of copies.
        builder.Services.TryAddSingleton<SagaCommitDelegate>(sp => {
            var handler = sp.GetRequiredService<MsspCommitHandler>();
            return uow => handler.CommitAsync(uow, SerializeLink);
        });

        // Parked-message sink — Replace overrides the LoggingParkedMessageSink fallback registered
        // by AddSagas, regardless of the order in which AddMssp was called.
        builder.Services.Replace(ServiceDescriptor.Singleton<IParkedMessageSink, MsspParkedMessageSink>());

        return new SagasMsspBuilder(builder.Services);
    }

    /// <summary>
    /// Serializes a saga change into the link stored in the saga stream.
    /// </summary>
    /// <exception cref="InvalidOperationException">
    /// Thrown when the change is not an <see cref="EventReference"/>.
    /// </exception>
    static EventData SerializeLink(object change) =>
        change is EventReference reference
            ? new EventData(EventReference.LinkEventType, reference.ToLinkData())
            : throw new InvalidOperationException($"A saga's changes are references to source events, but got a {change.GetType().Name}.");
}

sealed class SagasMsspBuilder(IServiceCollection services) : ISagasMsspBuilder {
    public IServiceCollection Services => services;
}
