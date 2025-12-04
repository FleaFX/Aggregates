using Aggregates.EventStoreDB.Serialization;
using Aggregates.EventStoreDB.Util;
using EventStore.Client;
using Microsoft.Extensions.DependencyInjection;
using System.Reflection;
using Aggregates.EventStoreDB.Extensions;
using Microsoft.Extensions.Logging;
using Aggregates.Configuration;
using Aggregates.Sagas;

namespace Aggregates.EventStoreDB.Workers;
/// <summary>
/// Initializes a new <see cref="SagaWorker{TSaga,TSagaState,TSagaEvent,TCommand,TCommandState,TCommandEvent}"/>.
/// </summary>
/// <param name="serviceScopeFactory">A <see cref="IServiceScopeFactory"/> that creates a scope in order to resolve the dependencies.</param>
class SagaWorker<TSaga, TSagaState, TSagaEvent, TCommand, TCommandState, TCommandEvent>(IServiceScopeFactory serviceScopeFactory, ILogger<SagaWorker<TSaga, TSagaState, TSagaEvent, TCommand, TCommandState, TCommandEvent>> logger)
    : ScopedBackgroundService<ListToAllAsyncDelegate, CreateToAllAsyncDelegate, DeleteToAllAsyncDelegate, SubscribeToAll, ResolvedEventDeserializer, MetadataDeserializer, AggregatesOptions, TSaga, ISagaHandler<TSagaState, TSagaEvent, TCommand, TCommandState, TCommandEvent>>(serviceScopeFactory)
    where TSagaState : IState<TSagaState, TSagaEvent>
    where TCommand : ICommand<TCommandState, TCommandEvent>
    where TCommandState : IState<TCommandState, TCommandEvent> {
    protected override async Task ExecuteCoreAsync(
        ListToAllAsyncDelegate listToAllAsync,
        CreateToAllAsyncDelegate createToAllAsync,
        DeleteToAllAsyncDelegate deleteToAllAsync,
        SubscribeToAll subscribeToAll,
        ResolvedEventDeserializer deserializer,
        MetadataDeserializer metadataDeserializer,
        AggregatesOptions options,
        TSaga owner,
        ISagaHandler<TSagaState, TSagaEvent, TCommand, TCommandState, TCommandEvent> sagaHandler,
        CancellationToken stoppingToken) {

        var (subscriptionGroupName, @delegate, skipPosition) = await BootstrapAsync(owner, listToAllAsync, createToAllAsync, deleteToAllAsync, options, stoppingToken);
        if (subscriptionGroupName is null || @delegate is null)
            return;

        // now connect the subscription and start the saga
        await Task.Run(async () => {
            do {
                await using var subscription = subscribeToAll(subscriptionGroupName);

                try {
                    await foreach (var message in subscription.Messages.WithCancellation(stoppingToken)) {
                        switch (message) {
                            case PersistentSubscriptionMessage.Event @event when !Equals(@event.ResolvedEvent.OriginalPosition, skipPosition): {
                                try {
                                    logger.LogInformation("Received event {eventType} @ {position} in {subscriptionGroupName}", @event.ResolvedEvent.Event.EventType, @event.ResolvedEvent.Event.Position, subscriptionGroupName);

                                    using var linkEvent = new LinkEventScope(@event.ResolvedEvent);
                                    await sagaHandler.HandleAsync(
                                        @delegate,
                                        (TSagaEvent)deserializer.Deserialize(@event.ResolvedEvent),
                                        metadataDeserializer.Deserialize(@event.ResolvedEvent),
                                        stoppingToken
                                    );

                                    // notify EventStoreDB that we're done
                                    await subscription.Ack(@event.ResolvedEvent);

                                    logger.LogInformation("Ack'ed event {eventType} @ {position} in {subscriptionGroupName}", @event.ResolvedEvent.Event.EventType, @event.ResolvedEvent.Event.Position, subscriptionGroupName);
                                }
                                catch (Exception ex) {
                                    logger.LogError(ex, "Exception occurred during handling of {eventType} @ {position} in subscription {subscriptionGroupName}.", @event.ResolvedEvent.Event.EventType, @event.ResolvedEvent.Event.Position, subscriptionGroupName);
                                    await subscription.Nack(
                                        @event.RetryCount < 5
                                            ? PersistentSubscriptionNakEventAction.Retry
                                            : PersistentSubscriptionNakEventAction.Park, ex.ToString(),
                                        @event.ResolvedEvent
                                    );
                                }
                                break;
                            }

                            case PersistentSubscriptionMessage.Event @event: {
                                await subscription.Ack(@event.ResolvedEvent);
                                break;
                            }

                            case PersistentSubscriptionMessage.SubscriptionConfirmation confirmation:
                                logger.LogInformation($"Subscription to {confirmation.SubscriptionId} has been confirmed. Saga started.");
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

    static async Task<(string? subscriptionGroupName, SagaAsyncDelegate<TSagaState, TSagaEvent, TCommand, TCommandState, TCommandEvent>? @delegate, IPosition? skipPosition)> BootstrapAsync(TSaga owner, ListToAllAsyncDelegate listToAllAsync, CreateToAllAsyncDelegate createToAllAsync, DeleteToAllAsyncDelegate deleteToAllAsync, AggregatesOptions options, CancellationToken cancellationToken) {
        var ownerType = owner!.GetType();
        if (ownerType.GetCustomAttribute<SagaContractAttribute>() is not { } sagaContract)
            return (null, null, null);

        // find saga delegate
        var @delegate = (
                from method in ownerType.GetMethods(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.DeclaredOnly)
                where method.IsDelegate<SagaAsyncDelegate<TSagaState, TSagaEvent, TCommand, TCommandState, TCommandEvent>>(owner)
                select method.CreateDelegate<SagaAsyncDelegate<TSagaState, TSagaEvent, TCommand, TCommandState, TCommandEvent>>(owner)
            ).Concat(
                from method in ownerType.GetMethods(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.DeclaredOnly)
                where method.IsDelegate<SagaDelegate<TSagaState, TSagaEvent, TCommand, TCommandState, TCommandEvent>>(owner)
                let dlgt = method.CreateDelegate<SagaDelegate<TSagaState, TSagaEvent, TCommand, TCommandState, TCommandEvent>>(owner)
                select new SagaAsyncDelegate<TSagaState, TSagaEvent, TCommand, TCommandState, TCommandEvent>((state, @event, metadata, _) => dlgt(state, @event, metadata).ToAsyncEnumerable())
            ).SingleOrDefault();
        if (@delegate is null)
            return (null, null, null);

        // find subscription and create it if it doesn't exist
        var subscription = (await listToAllAsync(cancellationToken: cancellationToken)).FirstOrDefault(info => info.GroupName == sagaContract.ToString());
        if (subscription != null) return (subscription.GroupName, @delegate, null);

        // find applicable event types by tentatively applying them to the state
        var eventTypes = (
            from assembly in options.Assemblies ?? AppDomain.CurrentDomain.GetAssemblies()
            from type in assembly.GetTypes()
            let attr = type.GetCustomAttribute<EventContractAttribute>()
            where type.IsAssignableTo(typeof(TSagaEvent)) && attr != null
            select (type, attr)
        ).TrySelect(tuple => {
            var (eventType, contract) = tuple;
            var _ = TSagaState.Initial.Apply((TSagaEvent)Activator.CreateInstance(eventType)!);
            return contract;
        });

        // create a persistent subscription with a filter on event type
        var filter = string.Join('|', eventTypes.Select(eventType => eventType.ToString().Replace(".", @"\.")));

        // find the preceding subscription, if any
        IPosition position = sagaContract.StartFromEnd ? Position.End : Position.Start;
        IPosition? skipPosition = null;
        if (sagaContract.ContinueFrom is { } continueFrom && (await listToAllAsync(cancellationToken: cancellationToken)).FirstOrDefault(info => info.GroupName == continueFrom) is { } preceding) {
            skipPosition = preceding.Stats.LastKnownEventPosition;

            // use its last known event position as the new starting point, otherwise just start from the beginning
            position = preceding.Stats.LastKnownEventPosition ?? position;

            // delete the preceding subscription
            await deleteToAllAsync(preceding.GroupName, cancellationToken: cancellationToken);
        }

        // now create the new subscription
        await createToAllAsync(sagaContract.ToString(), EventTypeFilter.RegularExpression($"^(?:{filter})$"), new PersistentSubscriptionSettings(startFrom: position), cancellationToken: cancellationToken);

        return (sagaContract.ToString(), @delegate, skipPosition);
    }
}