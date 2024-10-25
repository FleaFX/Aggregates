using Aggregates.EventStoreDB.Extensions;
using EventStore.Client;
using Microsoft.Extensions.DependencyInjection;
using System.Reflection;
using Aggregates.EventStoreDB.Serialization;
using Aggregates.EventStoreDB.Util;
using Aggregates.Metadata;
using Grpc.Core;
using Microsoft.Extensions.Logging;
using Aggregates.Configuration;

namespace Aggregates.EventStoreDB.Workers;

/// <summary>
/// Initializes a new <see cref="ReactionWorker{TReaction,TReactionEvent,TCommand,TState,TEvent}"/>.
/// </summary>
/// <param name="serviceScopeFactory">A <see cref="IServiceScopeFactory"/> that creates a scope in order to resolve the dependencies.</param>
class ReactionWorker<TReaction, TReactionEvent, TCommand, TState, TEvent>(IServiceScopeFactory serviceScopeFactory, ILogger<ReactionWorker<TReaction, TReactionEvent, TCommand, TState, TEvent>> logger)
    : ScopedBackgroundService<ListToAllAsyncDelegate, CreateToAllAsyncDelegate, SubscribeToAll, ResolvedEventDeserializer, MetadataDeserializer, AggregatesOptions, IReaction<TReactionEvent, TCommand, TState, TEvent>>(serviceScopeFactory) where TReaction : IReaction<TReactionEvent, TCommand, TState, TEvent>
    where TState : IState<TState, TEvent>
    where TCommand : ICommand<TState, TEvent> {
    readonly string _persistentSubscriptionGroupName = typeof(TReaction).FullName!;

    /// <summary>
    /// Implement this method rather than <see cref="ScopedBackgroundService{TDep1,TDep2,TDep3,TDep4,TDep5}.ExecuteAsync"/>. The required dependencies are passed in.
    /// </summary>
    /// <param name="listToAllAsync">Asynchronously lists persistent subscriptions to $all.</param>
    /// <param name="createToAllAsync">Asynchronously creates a filtered persistent subscription to $all.</param>
    /// <param name="subscribeToAll">Asynchronously subscribes to a persistent subscription to $all.</param>
    /// <param name="deserializer">Deserializes a <see cref="ResolvedEvent"/>.</param>
    /// <param name="reaction">The reaction to work.</param>
    /// <param name="stoppingToken">A <see cref="CancellationToken"/> that is signaled when the asynchronous operation should be stopped.</param>
    /// <returns>A <see cref="Task"/> that represents the asynchronous operation.</returns>
    protected override async Task ExecuteCoreAsync(ListToAllAsyncDelegate listToAllAsync, CreateToAllAsyncDelegate createToAllAsync, SubscribeToAll subscribeToAll, ResolvedEventDeserializer deserializer, MetadataDeserializer metadataDeserializer, AggregatesOptions options, IReaction<TReactionEvent, TCommand, TState, TEvent> reaction, CancellationToken stoppingToken) {
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
                from assembly in options.Assemblies ?? AppDomain.CurrentDomain.GetAssemblies()
                from type in assembly.GetTypes()
                let attr = type.GetCustomAttribute<EventContractAttribute>()
                where type.IsAssignableTo(typeof(TReactionEvent)) && attr != null
                select (type, attr)
            ).TrySelect(tuple => {
                var (eventType, contract) = tuple;
                _ = reaction.React((TReactionEvent)Activator.CreateInstance(eventType)!).ToArray();
                return contract;
            })

            // finally create a persistent subscription with a filter on event type
            let filter = string.Join('|', eventTypes.Select(eventType => eventType.ToString().Replace(".", @"\.")))
            select createToAllAsync(_persistentSubscriptionGroupName, EventTypeFilter.RegularExpression($"^(?:{filter})$"), new PersistentSubscriptionSettings(startFrom: Position.Start), cancellationToken: stoppingToken)
        );

        // now connect the subscription and start updating the projection state
        await Task.Run(async () => {
            do {
                await using var subscription = subscribeToAll(_persistentSubscriptionGroupName);

                try {
                    await foreach (var message in subscription.Messages.WithCancellation(stoppingToken)) {
                        switch (message) {
                            case PersistentSubscriptionMessage.Event @event: {
                                // react to the event and handle each command
                                try {
                                    await using var metadataScope = new MetadataScope();
                                    var metadata = metadataDeserializer.Deserialize(@event.ResolvedEvent);
                                    foreach (var pair in metadata ?? new Dictionary<string, object?>()) {
                                        metadataScope.Add(pair);
                                    }

                                    await foreach (var command in reaction.ReactAsync(
                                                       (TReactionEvent)deserializer.Deserialize(@event.ResolvedEvent),
                                                       metadata, stoppingToken)) {
                                        using var scope = ServiceScopeFactory.CreateScope();
                                        var commandHandler = scope.ServiceProvider
                                            .GetRequiredService<ICommandHandler<TCommand, TState, TEvent>>();
                                        await commandHandler.HandleAsync(command);
                                    }

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
                                    $"Subscription to {confirmation.SubscriptionId} has been confirmed. Reaction started.");
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