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
///   <item>Calls <see cref="IPolicyHandler{TEvent}.HandleAsync"/> when the event matches <typeparamref name="TEvent"/>, with automatic retry and parked-message fallback via <see cref="SubscriptionRetryPolicy"/>.</item>
///   <item>Records the stream position with a <see cref="CheckpointTracker"/>; checkpoints are written in batches, as configured by <see cref="SubscriptionCheckpointOptions"/>, and once more when the subscription stops.</item>
/// </list>
/// A fresh DI scope is created per event so that scoped services are never captured as
/// singletons by this long-lived hosted service.
/// </remarks>
sealed class PolicySubscriptionService<TEvent>(
    ISubscriptionFactory subscriptionFactory,
    IServiceScopeFactory scopeFactory,
    ICheckpointStore checkpointStore,
    SubscriptionRetryPolicy retryPolicy,
    SubscriptionCheckpointOptions checkpointOptions,
    TimeProvider timeProvider,
    string subscriptionId,
    bool startFromEnd) : BackgroundService {

    static readonly TimeSpan FinalFlushTimeout = TimeSpan.FromSeconds(5);

    /// <inheritdoc/>
    protected override async Task ExecuteAsync(CancellationToken stoppingToken) {
        var checkpoint = await checkpointStore.GetAsync(subscriptionId, stoppingToken);

        await using var subscription = subscriptionFactory.Subscribe(checkpoint, startFromEnd, stoppingToken);

        var tracker = new CheckpointTracker(checkpointStore, subscriptionId, checkpointOptions, timeProvider);
        try {
            await foreach (var message in subscription.WithCancellation(stoppingToken)) {
                if (message.Event is TEvent typedEvent) {
                    await retryPolicy.ExecuteAsync(async ct => {
                        await using var scope = scopeFactory.CreateAsyncScope();
                        var handler = scope.ServiceProvider.GetRequiredService<IPolicyHandler<TEvent>>();
                        // Seed the metadata scope with the incoming event's metadata so that commands
                        // dispatched by the policy inherit correlation/causation identifiers.
                        await using var metadataScope = new MetadataScope(message.Metadata);
                        await handler.HandleAsync(typedEvent, ct);
                    }, subscriptionId, message, stoppingToken);
                }

                await tracker.AdvanceAsync(message.CommitPosition, stoppingToken);
            }
        } finally {
            // stoppingToken is already cancelled on a graceful shutdown.
            using var flushTimeout = new CancellationTokenSource(FinalFlushTimeout);
            await tracker.FlushAsync(flushTimeout.Token);
        }
    }
}
