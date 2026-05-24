using Aggregates.Subscriptions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace Aggregates.Policies;

/// <summary>
/// A hosted service that subscribes to an event stream and routes incoming events to the
/// registered policy handler.
/// </summary>
/// <remarks>
/// For each received event the service:
/// <list type="number">
///   <item>Calls <see cref="IPolicyHandler{TEvent}.HandleAsync"/> when the event matches <typeparamref name="TEvent"/>.</item>
///   <item>Stores the stream position as the new checkpoint.</item>
/// </list>
/// A fresh DI scope is created per event so that scoped services are never captured as
/// singletons by this long-lived hosted service.
/// </remarks>
sealed class PolicySubscriptionService<TEvent>(
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
                var handler = scope.ServiceProvider.GetRequiredService<IPolicyHandler<TEvent>>();
                // Seed the metadata scope with the incoming event's metadata so that commands
                // dispatched by the policy inherit correlation/causation identifiers.
                await using var metadataScope = new MetadataScope(message.Metadata);
                await handler.HandleAsync(typedEvent, stoppingToken);
            }

            await checkpointStore.StoreAsync(subscriptionId, message.CommitPosition, stoppingToken);
        }
    }
}
