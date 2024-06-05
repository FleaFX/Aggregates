using Aggregates.EventStoreDB.Serialization;
using Aggregates.EventStoreDB.Util;
using EventStore.Client;
using Microsoft.Extensions.DependencyInjection;
using System.Reflection;
using Aggregates.EventStoreDB.Extensions;
using Grpc.Core;
using Microsoft.Extensions.Logging;

namespace Aggregates.EventStoreDB.Workers;
/// <summary>
/// Initializes a new <see cref="SagaWorker{TReactionState,TReactionEvent,TCommand,TCommandState,TCommandEvent}"/>.
/// </summary>
/// <param name="serviceScopeFactory">A <see cref="IServiceScopeFactory"/> that creates a scope in order to resolve the dependencies.</param>
class SagaWorker<TReactionState, TReactionEvent, TCommand, TCommandState, TCommandEvent>(IServiceScopeFactory serviceScopeFactory, ILogger<SagaWorker<TReactionState, TReactionEvent, TCommand, TCommandState, TCommandEvent>> logger)
    : ScopedBackgroundService<ListToAllAsyncDelegate, CreateToAllAsyncDelegate, SubscribeToAll, ResolvedEventDeserializer, MetadataDeserializer, ReactionsOptions, IReaction<TReactionState, TReactionEvent, TCommand, TCommandState, TCommandEvent>, ISagaHandler<TReactionState, TReactionEvent, TCommand, TCommandState, TCommandEvent>>(serviceScopeFactory)
    where TReactionState : IState<TReactionState, TReactionEvent>
    where TCommand : ICommand<TCommandState, TCommandEvent>
    where TCommandState : IState<TCommandState, TCommandEvent> {
    protected override async Task ExecuteCoreAsync(ListToAllAsyncDelegate listToAllAsync, CreateToAllAsyncDelegate createToAllAsync, SubscribeToAll subscribeToAll, ResolvedEventDeserializer deserializer, MetadataDeserializer metadataDeserializer, ReactionsOptions options, IReaction<TReactionState, TReactionEvent, TCommand, TCommandState, TCommandEvent> reaction, ISagaHandler<TReactionState, TReactionEvent, TCommand, TCommandState, TCommandEvent> sagaHandler, CancellationToken stoppingToken) {
        // setup a new subscription group if it doesn't exist yet
        var persistentSubscriptionGroupName = reaction.GetType().FullName!;
        await Task.WhenAll(
            from sub in (
                from i in await listToAllAsync(cancellationToken: stoppingToken)
                where i.GroupName == persistentSubscriptionGroupName
                select i
            ).DefaultIfEmpty()
            where sub == null

            // find applicable event types by tentatively applying them to both the state
            let eventTypes = (
                from assembly in options.Assemblies ?? AppDomain.CurrentDomain.GetAssemblies()
                where !(assembly.GetName().Name?.Contains("Microsoft.Data.SqlClient") ?? false)
                from type in assembly.GetTypes()
                let attr = type.GetCustomAttribute<EventContractAttribute>()
                where type.IsAssignableTo(typeof(TReactionEvent)) && attr != null
                select (type, attr)
            ).TrySelect(tuple => {
                var (eventType, contract) = tuple;
                var _ = TReactionState.Initial.Apply((TReactionEvent)Activator.CreateInstance(eventType)!);
                return contract;
            })

            // finally create a persistent subscription with a filter on event type
            let filter = string.Join('|', eventTypes.Select(eventType => eventType.ToString().Replace(".", @"\.")))
            select createToAllAsync(persistentSubscriptionGroupName, EventTypeFilter.RegularExpression($"^(?:{filter})$"), new PersistentSubscriptionSettings(), cancellationToken: stoppingToken)
        );

        // now connect the subscription and start the saga
        await Task.Run(async () => {
            do {
                await using var subscription = subscribeToAll(persistentSubscriptionGroupName);

                try {
                    await foreach (var message in subscription.Messages.WithCancellation(stoppingToken)) {
                        switch (message) {
                            case PersistentSubscriptionMessage.Event @event: {
                                try {
                                    using var linkEvent = new LinkEventScope(@event.ResolvedEvent);
                                    await sagaHandler.HandleAsync(
                                        (TReactionEvent)deserializer.Deserialize(@event.ResolvedEvent),
                                        metadataDeserializer.Deserialize(@event.ResolvedEvent), stoppingToken);

                                    // notify EventStoreDB that we're done
                                    await subscription.Ack(@event.ResolvedEvent);
                                }
                                catch (Exception ex) {
                                    await subscription.Nack(
                                        @event.RetryCount < 5
                                            ? PersistentSubscriptionNakEventAction.Retry
                                            : PersistentSubscriptionNakEventAction.Park, ex.ToString(),
                                        @event.ResolvedEvent);
                                }
                            }
                                break;

                            case PersistentSubscriptionMessage.SubscriptionConfirmation confirmation:
                                logger.LogInformation(
                                    $"Subscription to {confirmation.SubscriptionId} has been confirmed. Saga started.");
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