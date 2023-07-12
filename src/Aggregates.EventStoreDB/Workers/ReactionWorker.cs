using Aggregates.EventStoreDB.Extensions;
using Aggregates.Types;
using EventStore.Client;
using Microsoft.Extensions.DependencyInjection;
using System.Reflection;
using Aggregates.EventStoreDB.Serialization;
using Aggregates.EventStoreDB.Util;

namespace Aggregates.EventStoreDB.Workers;

class ReactionWorker<TReaction, TReactionEvent, TCommand, TState, TEvent>
    : ScopedBackgroundService<IServiceScopeFactory, ListToAllAsyncDelegate, CreateToAllAsyncDelegate, SubscribeToAllAsync, ResolvedEventDeserializer, MetadataDeserializer, IReaction<TReactionEvent, TCommand, TState, TEvent>> where TReaction : IReaction<TReactionEvent, TCommand, TState, TEvent>
    where TState : IState<TState, TEvent>
    where TCommand : ICommand<TCommand, TState, TEvent>
{
    readonly string _persistentSubscriptionGroupName = typeof(TReaction).FullName!;

    /// <summary>
    /// Initializes a new <see cref="ProjectionWorker{TState,TEvent}"/>.
    /// </summary>
    /// <param name="serviceScopeFactory">A <see cref="IServiceScopeFactory"/> that creates a scope in order to resolve the dependencies.</param>
    public ReactionWorker(IServiceScopeFactory serviceScopeFactory) : base(serviceScopeFactory) { }

    /// <summary>
    /// Implement this method rather than <see cref="ScopedBackgroundService{TDep1,TDep2,TDep3,TDep4,TDep5}.ExecuteAsync"/>. The required dependencies are passed in.
    /// </summary>
    /// <param name="serviceScopeFactory">A <see cref="IServiceScopeFactory"/> to use when creating scopes for each command handler.</param>
    /// <param name="listToAllAsync">Asynchronously lists persistent subscriptions to $all.</param>
    /// <param name="createToAllAsync">Asynchronously creates a filtered persistent subscription to $all.</param>
    /// <param name="subscribeToAllAsync">Asynchronously subscribes to a persistent subscription to $all.</param>
    /// <param name="deserializer">Deserializes a <see cref="ResolvedEvent"/>.</param>
    /// <param name="reaction">The reaction to work.</param>
    /// <param name="stoppingToken">A <see cref="CancellationToken"/> that is signaled when the asynchronous operation should be stopped.</param>
    /// <returns>A <see cref="Task"/> that represents the asynchronous operation.</returns>
    protected override async Task ExecuteCoreAsync(IServiceScopeFactory serviceScopeFactory, ListToAllAsyncDelegate listToAllAsync, CreateToAllAsyncDelegate createToAllAsync, SubscribeToAllAsync subscribeToAllAsync, ResolvedEventDeserializer deserializer, MetadataDeserializer metadataDeserializer, IReaction<TReactionEvent, TCommand, TState, TEvent> reaction, CancellationToken stoppingToken)
    {
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
                from type in assembly.GetTypes()
                let attr = type.GetCustomAttribute<EventContractAttribute>()
                where type.IsAssignableTo(typeof(TReactionEvent)) && attr != null
                select (type, attr)
            ).TrySelect(tuple =>
            {
                var (eventType, contract) = tuple;
                var _ = reaction.React((TReactionEvent)Activator.CreateInstance(eventType)!).ToArray();
                return contract;
            })

            // finally create a persistent subscription with a filter on event type
            let filter = string.Join('|', eventTypes.Select(eventType => eventType.ToString().Replace(".", @"\.")))
            select createToAllAsync(_persistentSubscriptionGroupName, EventTypeFilter.RegularExpression($"^(?:{filter})$"), new PersistentSubscriptionSettings(), cancellationToken: stoppingToken)
        );

        //now connect the subscription and start updating the projection state
        using var _ = await subscribeToAllAsync(_persistentSubscriptionGroupName,
            async (subscription, @event, retryCount, _) =>
            {
                try
                {
                    // react to the event and handle each command
                    await foreach (var command in reaction.ReactAsync(
                                       (TReactionEvent)deserializer.Deserialize(@event),
                                       metadataDeserializer.Deserialize(@event),
                                       stoppingToken))
                    {
                        using var scope = serviceScopeFactory.CreateScope();
                        var commandHandler = scope.ServiceProvider.GetRequiredService<ICommandHandler<TCommand, TState, TEvent>>();
                        await commandHandler.HandleAsync(command);
                    }

                    // notify EventStoreDB that we're done
                    await subscription.Ack(@event);
                }
                catch (Exception ex)
                {
                    await subscription.Nack(
                        retryCount < 5 ? PersistentSubscriptionNakEventAction.Retry : PersistentSubscriptionNakEventAction.Park,
                        ex.Message, @event);
                }
            },
            cancellationToken: stoppingToken);

        await stoppingToken;
    }
}