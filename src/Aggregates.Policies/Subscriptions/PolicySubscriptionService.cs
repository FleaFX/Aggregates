using Aggregates.Subscriptions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace Aggregates.Policies;

/// <summary>
/// A hosted service that subscribes to an event stream and routes incoming events to the
/// registered policy handler.
/// </summary>
/// <remarks>
/// The subscription runs in a <see cref="SubscriptionLoop"/>, which subscribes again after a
/// transient failure, parks messages that could not be deserialized, and records checkpoints.
/// For each event that matches <typeparamref name="TEvent"/>, the service calls
/// <see cref="IPolicyHandler{TEvent}.HandleAsync"/>, with automatic retry and parked-message
/// fallback via <see cref="SubscriptionRetryPolicy"/>.
/// A fresh DI scope is created per event so that scoped services are never captured as
/// singletons by this long-lived hosted service.
/// </remarks>
sealed class PolicySubscriptionService<TEvent>(
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
            var handler = scope.ServiceProvider.GetRequiredService<IPolicyHandler<TEvent>>();
            // Seed the metadata scope with the incoming event's metadata so that commands
            // dispatched by the policy inherit correlation/causation identifiers.
            await using var metadataScope = new MetadataScope(message.Metadata);
            await handler.HandleAsync(typedEvent, ct);
        }, subscriptionId, message, cancellationToken);
    }
}
