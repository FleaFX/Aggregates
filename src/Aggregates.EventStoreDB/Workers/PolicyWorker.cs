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
using Aggregates.Policies;
using Aggregates.Sagas;

namespace Aggregates.EventStoreDB.Workers;

/// <summary>
/// Initializes a new <see cref="PolicyWorker{TPolicy,TPolicyEvent,TCommand,TState,TEvent}"/>.
/// </summary>
/// <param name="serviceScopeFactory">A <see cref="IServiceScopeFactory"/> that creates a scope in order to resolve the dependencies.</param>
class PolicyWorker<TPolicy, TPolicyEvent, TCommand, TState, TEvent>(IServiceScopeFactory serviceScopeFactory, ILogger<PolicyWorker<TPolicy, TPolicyEvent, TCommand, TState, TEvent>> logger)
    : ScopedBackgroundService<ListToAllAsyncDelegate, CreateToAllAsyncDelegate, DeleteToAllAsyncDelegate, SubscribeToAll, ResolvedEventDeserializer, MetadataDeserializer, AggregatesOptions, TPolicy>(serviceScopeFactory)
    where TState : IState<TState, TEvent>
    where TCommand : ICommand<TState, TEvent> {
    /// <summary>
    /// Implement this method rather than <see cref="ScopedBackgroundService{TDep1,TDep2,TDep3,TDep4,TDep5}.ExecuteAsync"/>. The required dependencies are passed in.
    /// </summary>
    /// <param name="listToAllAsync">Asynchronously lists persistent subscriptions to $all.</param>
    /// <param name="createToAllAsync">Asynchronously creates a filtered persistent subscription to $all.</param>
    /// <param name="subscribeToAll">Asynchronously subscribes to a persistent subscription to $all.</param>
    /// <param name="deserializer">Deserializes a <see cref="ResolvedEvent"/>.</param>
    /// <param name="owner">The reaction to work.</param>
    /// <param name="stoppingToken">A <see cref="CancellationToken"/> that is signaled when the asynchronous operation should be stopped.</param>
    /// <returns>A <see cref="Task"/> that represents the asynchronous operation.</returns>
    protected override async Task ExecuteCoreAsync(
        ListToAllAsyncDelegate listToAllAsync,
        CreateToAllAsyncDelegate createToAllAsync,
        DeleteToAllAsyncDelegate deleteToAllAsync,
        SubscribeToAll subscribeToAll,
        ResolvedEventDeserializer deserializer,
        MetadataDeserializer metadataDeserializer,
        AggregatesOptions options,
        TPolicy owner,
        CancellationToken stoppingToken) {

        var (subscriptionGroupName, @delegate, skipPosition) = await BootstrapAsync(owner, listToAllAsync, createToAllAsync, deleteToAllAsync, options, stoppingToken);
        if (owner is null || @delegate is null)
            return;

        // now connect the subscription and start updating the projection state
        await Task.Run(async () => {
            do {
                await using var subscription = subscribeToAll(subscriptionGroupName);

                try {
                    await foreach (var message in subscription.Messages.WithCancellation(stoppingToken)) {
                        switch (message) {
                            case PersistentSubscriptionMessage.Event @event when !Equals(@event.ResolvedEvent.OriginalPosition, skipPosition): {
                                // react to the event and handle each command
                                try {
                                    await using var metadataScope = new MetadataScope();
                                    var metadata = metadataDeserializer.Deserialize(@event.ResolvedEvent);
                                    foreach (var pair in metadata ?? new Dictionary<string, object?>()) {
                                        metadataScope.Add(pair);
                                    }

                                    await foreach (var command in @delegate((TPolicyEvent)deserializer.Deserialize(@event.ResolvedEvent), metadata, stoppingToken)) {
                                        using var scope = ServiceScopeFactory.CreateScope();
                                        var commandHandler = scope.ServiceProvider.GetRequiredService<ICommandHandler<TCommand, TState, TEvent>>();
                                        await commandHandler.HandleAsync(command);
                                    }

                                    // notify EventStoreDB that we're done
                                    await subscription.Ack(@event.ResolvedEvent);
                                }
                                catch (Exception ex) {
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
                                logger.LogInformation($"Subscription to {confirmation.SubscriptionId} has been confirmed. Policy started.");
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

    static async Task<(string? subscriptionGroupName, PolicyAsyncDelegate<TPolicyEvent, TCommand, TState, TEvent>? @delegate, IPosition? skipPosition)> BootstrapAsync(TPolicy owner, ListToAllAsyncDelegate listToAllAsync, CreateToAllAsyncDelegate createToAllAsync, DeleteToAllAsyncDelegate deleteToAllAsync, AggregatesOptions options, CancellationToken cancellationToken) {
        var ownerType = owner!.GetType();
        if (ownerType.GetCustomAttribute<PolicyContractAttribute>() is not { } policyContract)
            return (null, null, null);

        // find saga delegate
        var @delegate = (
                from method in ownerType.GetMethods(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.DeclaredOnly)
                where method.IsDelegate<PolicyAsyncDelegate<TPolicyEvent, TCommand, TState, TEvent>>(owner)
                select method.CreateDelegate<PolicyAsyncDelegate<TPolicyEvent, TCommand, TState, TEvent>>(owner)
            ).Concat(
                from method in ownerType.GetMethods(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.DeclaredOnly)
                where method.IsDelegate<PolicyDelegate<TPolicyEvent, TCommand, TState, TEvent>>(owner)
                let dlgt = method.CreateDelegate<PolicyDelegate<TPolicyEvent, TCommand, TState, TEvent>>(owner)
                select new PolicyAsyncDelegate<TPolicyEvent, TCommand, TState, TEvent>((@event, metadata, _) => dlgt(@event, metadata).ToAsyncEnumerable())
            ).SingleOrDefault();
        if (@delegate is null)
            return (null, null, null);

        // find subscription and create it if it doesn't exist
        var subscription = (await listToAllAsync(cancellationToken: cancellationToken)).FirstOrDefault(info => info.GroupName == policyContract.ToString());
        if (subscription != null) return (subscription.GroupName, @delegate, null);

        // find applicable event types by tentatively applying them to the state
        var eventTypes = (
            from assembly in options.Assemblies ?? AppDomain.CurrentDomain.GetAssemblies()
            from type in assembly.GetTypes()
            let attr = type.GetCustomAttribute<EventContractAttribute>()
            where type.IsAssignableTo(typeof(TPolicyEvent)) && attr != null
            select (type, attr)
        ).TrySelect(tuple => {
            var (eventType, contract) = tuple;
            _ = @delegate((TPolicyEvent)Activator.CreateInstance(eventType)!, cancellationToken: cancellationToken).ToBlockingEnumerable().ToArray();
            return contract;
        });

        // create a persistent subscription with a filter on event type
        var filter = string.Join('|', eventTypes.Select(eventType => eventType.ToString().Replace(".", @"\.")));

        // find the preceding subscription, if any
        IPosition position = Position.Start;
        IPosition? skipPosition = null;
        if (policyContract.ContinueFrom is { } continueFrom && (await listToAllAsync(cancellationToken: cancellationToken)).FirstOrDefault(info => info.GroupName == continueFrom) is { } preceding) {
            skipPosition = preceding.Stats.LastKnownEventPosition;

            // use its last known event position as the new starting point, otherwise just start from the beginning
            position = preceding.Stats.LastKnownEventPosition ?? Position.Start;

            // delete the preceding subscription
            await deleteToAllAsync(preceding.GroupName, cancellationToken: cancellationToken);
        }

        // now create the new subscription
        await createToAllAsync(policyContract.ToString(), EventTypeFilter.RegularExpression($"^(?:{filter})$"), new PersistentSubscriptionSettings(startFrom: position), cancellationToken: cancellationToken);

        return (policyContract.ToString(), @delegate, skipPosition);
    }
}