using System.Reflection;
using Aggregates.Configuration;
using Aggregates.EventStoreDB.Extensions;
using Aggregates.EventStoreDB.Serialization;
using Aggregates.EventStoreDB.Util;
using Aggregates.Projections;
using EventStore.Client;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace Aggregates.EventStoreDB.Workers;

class ProjectionDelegateWorker<TProjection, TEvent>(IServiceScopeFactory serviceScopeFactory, ILogger<ProjectionDelegateWorker<TProjection, TEvent>> logger)
    : ScopedBackgroundService<ListToAllAsyncDelegate, CreateToAllAsyncDelegate, DeleteToAllAsyncDelegate, SubscribeToAll, ResolvedEventDeserializer, MetadataDeserializer, AggregatesOptions, TProjection>(serviceScopeFactory) {
    protected override async Task ExecuteCoreAsync(
        ListToAllAsyncDelegate listToAllAsync,
        CreateToAllAsyncDelegate createToAllAsync,
        DeleteToAllAsyncDelegate deleteToAllAsync,
        SubscribeToAll subscribeToAll,
        ResolvedEventDeserializer eventDeserializer,
        MetadataDeserializer metadataDeserializer,
        AggregatesOptions options,
        TProjection owner,
        CancellationToken stoppingToken) {

        var (subscriptionGroupName, @delegate, skipPosition) = await BootstrapAsync(owner, listToAllAsync, createToAllAsync, deleteToAllAsync, options, stoppingToken);
        if (subscriptionGroupName is null || @delegate is null)
            return;

        // now connect the subscription and start updating the projection state
        await Task.Run(async () => {
            do {
                await using var subscription = subscribeToAll(subscriptionGroupName);

                try {
                    await foreach (var message in subscription.Messages.WithCancellation(stoppingToken)) {
                        switch (message) {
                            case PersistentSubscriptionMessage.Event @event when !Equals(@event.ResolvedEvent.OriginalPosition, skipPosition): {
                                try {
                                    logger.LogTrace("Received event {eventType} @ {position} in {subscriptionGroupName}", @event.ResolvedEvent.Event.EventType, @event.ResolvedEvent.Event.Position, subscriptionGroupName);

                                    // apply and commit the projection
                                    await @delegate(
                                        @event: (TEvent)eventDeserializer.Deserialize(@event.ResolvedEvent),
                                        metadata: metadataDeserializer.Deserialize(@event.ResolvedEvent)
                                    ).CommitAsync(stoppingToken);

                                    // notify EventStoreDB that we're done
                                    await subscription.Ack(@event.ResolvedEvent);

                                    logger.LogTrace("Ack'ed event {eventType} @ {position} in {subscriptionGroupName}", @event.ResolvedEvent.Event.EventType, @event.ResolvedEvent.Event.Position, subscriptionGroupName);
                                } catch (Exception ex) {
                                    logger.LogError(ex, "Exception occurred during handling of {eventType} @ {position} in subscription {subscriptionGroupName}.", @event.ResolvedEvent.Event.EventType, @event.ResolvedEvent.Event.Position, subscriptionGroupName);
                                        await subscription.Nack(
                                        @event.RetryCount < 5
                                            ? PersistentSubscriptionNakEventAction.Retry
                                            : PersistentSubscriptionNakEventAction.Park, ex.Message,
                                        @event.ResolvedEvent);
                                }
                                break;
                            }

                            case PersistentSubscriptionMessage.Event @event: {
                                await subscription.Ack(@event.ResolvedEvent);
                                break;
                            }

                            case PersistentSubscriptionMessage.SubscriptionConfirmation confirmation:
                                logger.LogInformation($"Subscription to {confirmation.SubscriptionId} has been confirmed. Projection started.");
                                break;
                        }
                    }
                }
                catch (Exception e) {
                    logger.LogError(e, e?.Message);
                }

                if (!stoppingToken.IsCancellationRequested) logger.LogWarning("Subscription has ended or has been dropped. Reconnecting.");
            } while (!stoppingToken.IsCancellationRequested);
        }, stoppingToken);

        await stoppingToken;
    }

    static async Task<(string? subscriptionGroupName, ProjectionDelegate<TEvent>? @delegate, IPosition? skipPosition)> BootstrapAsync(TProjection owner, ListToAllAsyncDelegate listToAllAsync, CreateToAllAsyncDelegate createToAllAsync, DeleteToAllAsyncDelegate deleteToAllAsync, AggregatesOptions options,CancellationToken cancellationToken) {
        var ownerType = owner!.GetType();
        if (ownerType.GetCustomAttribute<ProjectionContractAttribute>() is not { } projectionContract)
            return (null, null, null);

        // find projection delegate
        var @delegate = (
                from method in ownerType.GetMethods(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.DeclaredOnly)
                where method.IsDelegate<ProjectionDelegate<TEvent>>(owner)
                select method.CreateDelegate<ProjectionDelegate<TEvent>>(owner)
            ).SingleOrDefault();
        if (@delegate is null)
            return (null, null, null);

        // find subscription and create it if it doesn't exist
        var subscription = (await listToAllAsync(cancellationToken: cancellationToken)).FirstOrDefault(info => info.GroupName == projectionContract.ToString());
        if (subscription != null) return (subscription.GroupName, @delegate, null);
        
        // find applicable event types by tentatively applying them to the state
        var eventTypes = (
            from assembly in options.Assemblies ?? AppDomain.CurrentDomain.GetAssemblies()
            from type in assembly.GetTypes()
            let attr = type.GetCustomAttribute<EventContractAttribute>()
            where type.IsAssignableTo(typeof(TEvent)) && attr != null
            select (type, attr)
        ).TrySelect(tuple => {
            var (eventType, contract) = tuple;
            @delegate((TEvent)Activator.CreateInstance(eventType)!);
            return contract;
        });

        // create a persistent subscription with a filter on event type
        var filter = string.Join('|', eventTypes.Select(eventType => eventType.ToString().Replace(".", @"\.")));

        // find the preceding subscription, if any
        IPosition position = Position.Start;
        IPosition? skipPosition = null;
        if (projectionContract.ContinueFrom is { } continueFrom && (await listToAllAsync(cancellationToken: cancellationToken)).FirstOrDefault(info => info.GroupName == continueFrom) is {} preceding) {
            skipPosition = preceding.Stats.LastKnownEventPosition;

            // use its last known event position as the new starting point, otherwise just start from the beginning
            position = preceding.Stats.LastKnownEventPosition ?? Position.Start;

            // delete the preceding subscription
            await deleteToAllAsync(preceding.GroupName, cancellationToken: cancellationToken);
        }
        
        // now create the new subscription
        await createToAllAsync(projectionContract.ToString(), EventTypeFilter.RegularExpression($"^(?:{filter})$"), new PersistentSubscriptionSettings(startFrom: position), cancellationToken: cancellationToken);

        return (projectionContract.ToString(), @delegate, skipPosition);
    }
}