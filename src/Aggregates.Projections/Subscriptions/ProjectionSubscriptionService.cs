using Aggregates.Subscriptions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace Aggregates.Projections;

/// <summary>
/// A hosted service that subscribes to an event stream and routes incoming events to the
/// registered projection handler.
/// </summary>
/// <remarks>
/// For each received event the service:
/// <list type="number">
///   <item>Calls <see cref="IProjectionHandler{TEvent}.HandleAsync"/> when the event matches <typeparamref name="TEvent"/>.</item>
///   <item>Stores the stream position as the new checkpoint.</item>
/// </list>
/// A fresh DI scope is created per event so that scoped services are never captured as
/// singletons by this long-lived hosted service.
/// </remarks>
sealed class ProjectionSubscriptionService<TEvent>(
    ISubscriptionFactory subscriptionFactory,
    IServiceScopeFactory scopeFactory,
    ICheckpointStore checkpointStore,
    string subscriptionId,
    bool startFromEnd) : BackgroundService {

    /// <inheritdoc/>
    protected override async Task ExecuteAsync(CancellationToken stoppingToken) {
        var checkpoint = await checkpointStore.GetAsync(subscriptionId, stoppingToken);

        await using var subscription = subscriptionFactory.Subscribe(checkpoint, startFromEnd, stoppingToken);

        await foreach (var message in subscription.WithCancellation(stoppingToken)) {
            if (message.Event is TEvent typedEvent) {
                await using var scope = scopeFactory.CreateAsyncScope();
                var handler = scope.ServiceProvider.GetRequiredService<IProjectionHandler<TEvent>>();
                await handler.HandleAsync(typedEvent, stoppingToken);
            }

            await checkpointStore.StoreAsync(subscriptionId, message.CommitPosition, stoppingToken);
        }
    }
}
