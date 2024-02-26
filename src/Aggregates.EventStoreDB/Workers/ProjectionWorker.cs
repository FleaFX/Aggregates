using Aggregates.EventStoreDB.Extensions;
using Aggregates.Types;
using EventStore.Client;
using Microsoft.Extensions.DependencyInjection;
using System.Reflection;
using Aggregates.EventStoreDB.Serialization;
using Aggregates.EventStoreDB.Util;

namespace Aggregates.EventStoreDB.Workers;

class ProjectionWorker<TState, TEvent> : ScopedBackgroundService<ListToAllAsyncDelegate, CreateToAllAsyncDelegate, SubscribeToAllAsync, ResolvedEventDeserializer, MetadataDeserializer, IProjection<TState, TEvent>> where TState : IProjection<TState, TEvent> {
    readonly string _persistentSubscriptionGroupName = typeof(TState).FullName!;

    /// <summary>
    /// Initializes a new <see cref="ProjectionWorker{TState,TEvent}"/>.
    /// </summary>
    /// <param name="serviceScopeFactory">A <see cref="IServiceScopeFactory"/> that creates a scope in order to resolve the dependencies.</param>
    public ProjectionWorker(IServiceScopeFactory serviceScopeFactory) : base(serviceScopeFactory) { }

    /// <summary>
    /// Implement this method rather than <see cref="ScopedBackgroundService{TDep1,TDep2,TDep3,TDep4,TDep5}.ExecuteAsync"/>. The required dependencies are passed in.
    /// </summary>
    /// <param name="listToAllAsync">Asynchronously lists persistent subscriptions to $all.</param>
    /// <param name="createToAllAsync">Asynchronously creates a filtered persistent subscription to $all.</param>
    /// <param name="subscribeToAllAsync">Asynchronously subscribes to a persistent subscription to $all.</param>
    /// <param name="deserializer">Deserializes a <see cref="ResolvedEvent"/>.</param>
    /// <param name="initialState">The initial state of the projection.</param>
    /// <param name="stoppingToken">A <see cref="CancellationToken"/> that is signaled when the asynchronous operation should be stopped.</param>
    /// <returns>A <see cref="Task"/> that represents the asynchronous operation.</returns>
    protected override async Task ExecuteCoreAsync(ListToAllAsyncDelegate listToAllAsync, CreateToAllAsyncDelegate createToAllAsync, SubscribeToAllAsync subscribeToAllAsync, ResolvedEventDeserializer deserializer, MetadataDeserializer metadataDeserializer, IProjection<TState, TEvent> initialState, CancellationToken stoppingToken) {
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
            select createToAllAsync(_persistentSubscriptionGroupName, EventTypeFilter.RegularExpression($"^(?:{filter})$"), new PersistentSubscriptionSettings(), cancellationToken: stoppingToken)
        );

        //now connect the subscription and start updating the projection state
        var state = initialState;
        using var _ = await subscribeToAllAsync(_persistentSubscriptionGroupName,
            async (subscription, @event, retryCount, _) => {
                try {
                    // apply and commit the projection
                    state = await state
                        .Apply((TEvent)deserializer.Deserialize(@event), metadataDeserializer.Deserialize(@event))
                        .CommitAsync(stoppingToken);

                    // notify EventStoreDB that we're done
                    await subscription.Ack(@event);
                } catch (Exception ex) {
                    await subscription.Nack(
                        retryCount < 5 ? PersistentSubscriptionNakEventAction.Retry : PersistentSubscriptionNakEventAction.Park,
                        ex.Message, @event);
                }
            },
            cancellationToken: stoppingToken);

        await stoppingToken;
    }
}