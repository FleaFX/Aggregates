using Aggregates.KurrentDB;
using Aggregates.KurrentDB.Internal;
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
    ///     serializes the saga's changes as <c>$&gt;</c> links to their source events and as
    ///     deferred command and deadline requests, so a saga stream never holds copies of the events it reacts to.
    ///   </item>
    ///   <item><see cref="KurrentDbScheduledStore"/> as <see cref="IScheduledStore"/>.</item>
    /// </list>
    /// </remarks>
    public static ISagasKurrentDbBuilder AddKurrentDb(this ISagasBuilder builder) {
        // Checkpoint store
        builder.Services.TryAddSingleton<ICheckpointStore, KurrentDbCheckpointStore>();

        // Subscription factory
        builder.Services.TryAddSingleton<ISubscriptionFactory, KurrentDbSubscriptionFactory>();

        // Saga repository
        builder.UseSagaRepository(typeof(KurrentDbSagaRepository<,>));

        // Saga commit delegate — reuses the same KurrentDbCommitHandler as regular aggregates, but
        // serializes the saga's changes as links to their source events and as deferred commands and
        // deadlines, never as copies.
        builder.Services.TryAddSingleton<SagaCommitDelegate>(sp => {
            var handler = sp.GetRequiredService<KurrentDbCommitHandler>();
            var options = sp.GetRequiredService<KurrentDbOptions>();
            return uow => handler.CommitAsync(uow, change => SerializeChange(change, options));
        });

        // Parked-message sink — Replace overrides the LoggingParkedMessageSink fallback registered
        // by AddSagas, regardless of the order in which AddKurrentDb was called.
        builder.Services.Replace(ServiceDescriptor.Singleton<IParkedMessageSink, KurrentDbParkedMessageSink>());

        // Parked-message store — lists, replays and dismisses parked messages; shared with the other kinds
        builder.Services.TryAddSingleton<IParkedMessageStore, KurrentDbParkedMessageStore>();

        // Scheduled store — read by the scheduler of every saga and policy
        builder.Services.TryAddSingleton<IScheduledStore, KurrentDbScheduledStore>();

        return new SagasKurrentDbBuilder(builder.Services);
    }

    /// <summary>
    /// Serializes a saga change into the entry stored in the saga stream.
    /// </summary>
    /// <exception cref="InvalidOperationException">
    /// Thrown when the change is not an <see cref="EventReference"/>, a <see cref="DeferredCommandRequest"/>
    /// or a <see cref="DeadlineRequest"/>.
    /// </exception>
    static SerializedEvent SerializeChange(object change, KurrentDbOptions options) {
        switch (change) {
            case EventReference reference:
                return new SerializedEvent(EventReference.LinkEventType, reference.ToLinkData());
            case DeferredCommandRequest request:
                var command = options.Serialize!(request.Command);
                return new SerializedEvent(ScheduledFormat.CommandRequestEventType,
                    ScheduledFormat.WriteRequest(request.Owner, request.Due, command.EventType, command.Data.Span, request.Cause));
            case DeadlineRequest deadline:
                var payload = options.Serialize!(deadline.Payload);
                return new SerializedEvent(ScheduledFormat.DeadlineRequestEventType,
                    ScheduledFormat.WriteRequest(deadline.Owner, deadline.Due, payload.EventType, payload.Data.Span, deadline.Cause));
            default:
                throw new InvalidOperationException($"A saga's changes are links, deferred commands and deadlines, but got a {change.GetType().Name}.");
        }
    }
}

sealed class SagasKurrentDbBuilder(IServiceCollection services) : ISagasKurrentDbBuilder {
    public IServiceCollection Services => services;
}
