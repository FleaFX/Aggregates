using EventStore.Client;
using System.Reflection;
using Aggregates.EventStoreDB.Extensions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace Aggregates.EventStoreDB; 

class ProjectionWorker<TState, TEvent> : ScopedBackgroundService<ListToAllAsyncDelegate, CreateToAllAsyncDelegate, SubscribeToAllAsync, ResolvedEventDeserializer> where TState : IProjection<TState, TEvent> {
    readonly ILogger<ProjectionWorker<TState, TEvent>> _logger;
    readonly string _persistentSubscriptionGroupName = typeof(TState).FullName!;

    /// <summary>
    /// Initializes a new <see cref="ProjectionWorker{TState,TEvent}"/>.
    /// </summary>
    /// <param name="serviceScopeFactory">A <see cref="IServiceScopeFactory"/> that creates a scope in order to resolve the dependencies.</param>
    public ProjectionWorker(IServiceScopeFactory serviceScopeFactory, ILogger<ProjectionWorker<TState, TEvent>> logger) : base(serviceScopeFactory) =>
        _logger = logger;

    /// <summary>
    /// Implement this method rather than <see cref="ScopedBackgroundService{TDep1,TDep2,TDep3,TDep4}.ExecuteAsync"/>. The required dependencies are passed in.
    /// </summary>
    /// <param name="listToAllAsync">Asynchronously lists persistent subscriptions to $all.</param>
    /// <param name="createToAllAsync">Asynchronously creates a filtered persistent subscription to $all.</param>
    /// <param name="subscribeToAllAsync">Asynchronously subscribes to a persistent subscription to $all.</param>
    /// <param name="deserializer">Deserializes a <see cref="ResolvedEvent"/>.</param>
    /// <param name="stoppingToken">A <see cref="CancellationToken"/> that is signaled when the asynchronous operation should be stopped.</param>
    /// <returns>A <see cref="Task"/> that represents the asynchronous operation.</returns>
    protected override async Task ExecuteCoreAsync(ListToAllAsyncDelegate listToAllAsync, CreateToAllAsyncDelegate createToAllAsync, SubscribeToAllAsync subscribeToAllAsync, ResolvedEventDeserializer deserializer, CancellationToken stoppingToken) {
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
                where type.IsAssignableTo(typeof(TEvent)) && attr != null
                select (type, attr)
            ).TrySelect(tuple => {
                var (eventType, contract) = tuple;
                TState.Initial.Apply((TEvent)Activator.CreateInstance(eventType)!);
                return contract;
            })

            // finally create a persistent subscription with a filter on event type
            let filter = string.Join('|', eventTypes.Select(eventType => eventType.ToString().Replace(".", @"\.")))
            select createToAllAsync(_persistentSubscriptionGroupName, EventTypeFilter.RegularExpression($"^(?:{filter})$"), new PersistentSubscriptionSettings(), cancellationToken: stoppingToken)
                .ContinueWith(_ => _logger.LogDebug($"Persistent subscription to $all created: {_persistentSubscriptionGroupName} using filter: ^(?:{filter})$"), stoppingToken)
        );

        //now connect the subscription and start updating the projection state
        var state = TState.Initial;
        using var _ = await subscribeToAllAsync(_persistentSubscriptionGroupName,
            async (subscription, @event, retryCount, _) => {
                try {
                    _logger.LogDebug($"Event {@event.Event.EventType} appeared in subscription {subscription.SubscriptionId}");
                    state = state.Apply((TEvent)deserializer.Deserialize(@event));
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