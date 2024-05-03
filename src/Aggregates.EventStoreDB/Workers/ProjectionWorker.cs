using Aggregates.EventStoreDB.Extensions;
using EventStore.Client;
using Microsoft.Extensions.DependencyInjection;
using System.Reflection;
using Aggregates.EventStoreDB.Serialization;
using Aggregates.EventStoreDB.Util;
using Grpc.Core;
using Microsoft.Extensions.Logging;

namespace Aggregates.EventStoreDB.Workers;

/// <summary>
/// Initializes a new <see cref="ProjectionWorker{TState,TEvent}"/>.
/// </summary>
/// <param name="serviceScopeFactory">A <see cref="IServiceScopeFactory"/> that creates a scope in order to resolve the dependencies.</param>
class ProjectionWorker<TState, TEvent>(IServiceScopeFactory serviceScopeFactory, ILogger<ProjectionWorker<TState, TEvent>> logger) : ScopedBackgroundService<ListToAllAsyncDelegate, CreateToAllAsyncDelegate, SubscribeToAll, ResolvedEventDeserializer, MetadataDeserializer, IProjection<TState, TEvent>>(serviceScopeFactory) where TState : IProjection<TState, TEvent> {
    readonly string _persistentSubscriptionGroupName = typeof(TState).FullName!;

    /// <summary>
    /// Implement this method rather than <see cref="ScopedBackgroundService{TDep1,TDep2,TDep3,TDep4,TDep5}.ExecuteAsync"/>. The required dependencies are passed in.
    /// </summary>
    /// <param name="listToAllAsync">Asynchronously lists persistent subscriptions to $all.</param>
    /// <param name="createToAllAsync">Asynchronously creates a filtered persistent subscription to $all.</param>
    /// <param name="subscribeToAll">Asynchronously subscribes to a persistent subscription to $all.</param>
    /// <param name="deserializer">Deserializes a <see cref="ResolvedEvent"/>.</param>
    /// <param name="initialState">The initial state of the projection.</param>
    /// <param name="stoppingToken">A <see cref="CancellationToken"/> that is signaled when the asynchronous operation should be stopped.</param>
    /// <returns>A <see cref="Task"/> that represents the asynchronous operation.</returns>
    protected override async Task ExecuteCoreAsync(ListToAllAsyncDelegate listToAllAsync, CreateToAllAsyncDelegate createToAllAsync, SubscribeToAll subscribeToAll, ResolvedEventDeserializer deserializer, MetadataDeserializer metadataDeserializer, IProjection<TState, TEvent> initialState, CancellationToken stoppingToken) {
        // setup a new subscription group if it doesn't exist yet
        await Task.WhenAll(
            from sub in (
                from i in await listToAllAsync(cancellationToken: stoppingToken)
                where i.GroupName == _persistentSubscriptionGroupName
                select i
            ).DefaultIfEmpty()
            where sub == null

            // find applicable event types by tentatively applying them to the state
            let eventTypes = (
                from assembly in AppDomain.CurrentDomain.GetAssemblies()
                where !(assembly.GetName().Name?.Contains("Microsoft.Data.SqlClient") ?? false)
                from type in assembly.GetTypes()
                let attr = type.GetCustomAttribute<EventContractAttribute>()
                where type.IsAssignableTo(typeof(TEvent)) && attr != null
                select (type, attr)
            ).TrySelect(tuple => {
                var (eventType, contract) = tuple;
                initialState.Apply((TEvent)Activator.CreateInstance(eventType)!);
                return contract;
            })

            // finally create a persistent subscription with a filter on event type
            let filter = string.Join('|', eventTypes.Select(eventType => eventType.ToString().Replace(".", @"\.")))
            select createToAllAsync(_persistentSubscriptionGroupName, EventTypeFilter.RegularExpression($"^(?:{filter})$"), new PersistentSubscriptionSettings() , cancellationToken: stoppingToken)
        );

        // now connect the subscription and start updating the projection state
        await Task.Run(async () => {
            do {
                var state = initialState;
                await using var subscription = subscribeToAll(_persistentSubscriptionGroupName);

                try {
                    await foreach (var message in subscription.Messages.WithCancellation(stoppingToken)) {
                        switch (message) {
                            case PersistentSubscriptionMessage.Event @event: {
                                try {
                                    // apply and commit the projection
                                    state = await state.Apply((TEvent)deserializer.Deserialize(@event.ResolvedEvent),
                                            metadataDeserializer.Deserialize(@event.ResolvedEvent))
                                        .CommitAsync(stoppingToken);

                                    // notify EventStoreDB that we're done
                                    await subscription.Ack(@event.ResolvedEvent);
                                }
                                catch (Exception ex) {
                                    await subscription.Nack(
                                        @event.RetryCount < 5
                                            ? PersistentSubscriptionNakEventAction.Retry
                                            : PersistentSubscriptionNakEventAction.Park, ex.Message,
                                        @event.ResolvedEvent);
                                }
                            }
                                break;
                            case PersistentSubscriptionMessage.SubscriptionConfirmation confirmation:
                                logger.LogInformation(
                                    $"Subscription to {confirmation.SubscriptionId} has been confirmed. Projection started.");
                                break;
                        }
                    }
                }
                catch (RpcException e) {
                    logger.LogError(e, e?.Message);
                }

                if (!stoppingToken.IsCancellationRequested) logger.LogWarning("Subscription has ended or has been dropped. Reconnecting.");
            } while (!stoppingToken.IsCancellationRequested);
        }, stoppingToken);

        await stoppingToken;
    }
}