using Aggregates.Subscriptions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace Aggregates.Projections;

/// <summary>
/// A hosted service that subscribes to an event stream and routes incoming events to the
/// registered projection handler.
/// </summary>
/// <remarks>
/// The subscription runs in a <see cref="SubscriptionLoop"/>, which subscribes again after a
/// transient failure, parks messages that could not be deserialized, and records checkpoints.
/// For each event that matches <typeparamref name="TEvent"/>, the service calls
/// <see cref="IProjectionHandler{TEvent}.HandleAsync"/>, with automatic retry and parked-message
/// fallback via <see cref="SubscriptionRetryPolicy"/>.
/// A fresh DI scope is created per event so that scoped services are never captured as
/// singletons by this long-lived hosted service.
/// </remarks>
sealed class ProjectionSubscriptionService<TEvent>(
    SubscriptionLoop loop,
    IServiceScopeFactory scopeFactory,
    SubscriptionRetryPolicy retryPolicy,
    string subscriptionId,
    bool startFromEnd) : BackgroundService {

    /// <inheritdoc/>
    protected override Task ExecuteAsync(CancellationToken stoppingToken) =>
        loop.RunAsync(subscriptionId, startFromEnd, ProcessAsync, stoppingToken);

    async ValueTask ProcessAsync(SubscriptionMessage message, CancellationToken cancellationToken) {
        if (message.Event is not TEvent typedEvent)
            return;

        await retryPolicy.ExecuteAsync(async ct => {
            await using var scope = scopeFactory.CreateAsyncScope();
            var handler = scope.ServiceProvider.GetRequiredService<IProjectionHandler<TEvent>>();
            await handler.HandleAsync(typedEvent, message.Metadata, ct);
        }, subscriptionId, message, cancellationToken);
    }
}
