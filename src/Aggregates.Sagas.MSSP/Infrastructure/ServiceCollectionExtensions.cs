using Aggregates.MSSP;
using Aggregates.MSSP.Internal;
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
    ///     serializes the saga's changes as <c>$&gt;</c> links to their source events and as
    ///     deferred command and deadline requests, so a saga stream never holds copies of the events it reacts to.
    ///   </item>
    ///   <item><see cref="MsspScheduledStore"/> as <see cref="IScheduledStore"/>.</item>
    /// </list>
    /// </remarks>
    public static ISagasMsspBuilder AddMssp(this ISagasBuilder builder) {
        // Checkpoint store
        builder.Services.TryAddSingleton<ICheckpointStore, MsspCheckpointStore>();

        // Subscription factory
        builder.Services.TryAddSingleton<ISubscriptionFactory, MsspSubscriptionFactory>();

        // Saga repository
        builder.UseSagaRepository(typeof(MsspSagaRepository<,>));

        // Saga commit delegate — reuses the same MsspCommitHandler as regular aggregates, but
        // serializes the saga's changes as links to their source events and as deferred commands and
        // deadlines, never as copies.
        builder.Services.TryAddSingleton<SagaCommitDelegate>(sp => {
            var handler = sp.GetRequiredService<MsspCommitHandler>();
            var options = sp.GetRequiredService<MsspOptions>();
            return (uow, cancellationToken) => handler.CommitAsync(uow, change => SerializeChange(change, options), cancellationToken);
        });

        // Parked-message sink — Replace overrides the LoggingParkedMessageSink fallback registered
        // by AddSagas, regardless of the order in which AddMssp was called.
        builder.Services.Replace(ServiceDescriptor.Singleton<IParkedMessageSink, MsspParkedMessageSink>());

        // Parked-message store — lists, replays and dismisses parked messages; shared with the other kinds
        builder.Services.TryAddSingleton<IParkedMessageStore, MsspParkedMessageStore>();

        // Scheduled store — read by the scheduler of every saga and policy
        builder.Services.TryAddSingleton<IScheduledStore, MsspScheduledStore>();

        return new SagasMsspBuilder(builder.Services);
    }

    /// <summary>
    /// Serializes a saga change into the entry stored in the saga stream.
    /// </summary>
    /// <exception cref="InvalidOperationException">
    /// Thrown when the change is not an <see cref="EventReference"/>, a <see cref="DeferredCommandRequest"/>
    /// or a <see cref="DeadlineRequest"/>.
    /// </exception>
    static EventData SerializeChange(object change, MsspOptions options) {
        switch (change) {
            case EventReference reference:
                return new EventData(EventReference.LinkEventType, reference.ToLinkData());
            case DeferredCommandRequest request:
                var command = options.Serialize!(request.Command);
                return new EventData(ScheduledFormat.CommandRequestEventType,
                    ScheduledFormat.WriteRequest(request.Owner, request.Due, command.EventType, command.Data.Span, request.Cause));
            case DeadlineRequest deadline:
                var payload = options.Serialize!(deadline.Payload);
                return new EventData(ScheduledFormat.DeadlineRequestEventType,
                    ScheduledFormat.WriteRequest(deadline.Owner, deadline.Due, payload.EventType, payload.Data.Span, deadline.Cause));
            default:
                throw new InvalidOperationException($"A saga's changes are links, deferred commands and deadlines, but got a {change.GetType().Name}.");
        }
    }
}

sealed class SagasMsspBuilder(IServiceCollection services) : ISagasMsspBuilder {
    public IServiceCollection Services => services;
}
