using Aggregates.Subscriptions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace Aggregates.Policies;

/// <summary>
/// A hosted service that subscribes to an event stream and routes incoming events to
/// <typeparamref name="TPolicy"/>.
/// </summary>
/// <remarks>
/// The subscription runs in a <see cref="SubscriptionLoop"/>, which subscribes again after a
/// transient failure, parks messages that could not be deserialized, and records checkpoints.
/// For each event that matches <typeparamref name="TEvent"/>, the service resolves the handler
/// chain of <typeparamref name="TPolicy"/> and lets the policy react, with automatic retry and
/// parked-message fallback via <see cref="SubscriptionRetryPolicy"/>.
/// A fresh DI scope is created per event so that scoped services are never captured as
/// singletons by this long-lived hosted service.
/// Next to the subscription it runs the policy's <see cref="Scheduler"/>, which executes the
/// commands the policy deferred with <see cref="DeferredCommandExtensions.DeferUntil"/> when they
/// are due, with retries and parking under <c>scheduled-{subscription id}</c>.
/// </remarks>
/// <typeparam name="TPolicy">The policy class.</typeparam>
/// <typeparam name="TEvent">The event type the policy reacts to.</typeparam>
sealed class PolicySubscriptionService<TPolicy, TEvent>(
    SubscriptionLoop loop,
    IServiceScopeFactory scopeFactory,
    SubscriptionRetryPolicy retryPolicy,
    Scheduler scheduler,
    SchedulerActivation<TPolicy> activation,
    SubscriptionRegistration registration) : BackgroundService
    where TPolicy : IPolicy<TEvent> {

    /// <inheritdoc/>
    protected override async Task ExecuteAsync(CancellationToken stoppingToken) {
        using var stop = CancellationTokenSource.CreateLinkedTokenSource(stoppingToken);
        var events = loop.RunAsync(registration, ProcessAsync, stop.Token);
        var deferred = scheduler.RunAsync(registration, activation, stop.Token);
        // Each ends only when the host stops or after a failure that is not transient; the other stops with it.
        await Task.WhenAny(events, deferred);
        await stop.CancelAsync();
        await Task.WhenAll(events, deferred);
    }

    async ValueTask ProcessAsync(SubscriptionMessage message, CancellationToken cancellationToken) {
        if (message.Event is not TEvent typedEvent)
            return;

        await retryPolicy.ExecuteAsync(async ct => {
            await using var scope = scopeFactory.CreateAsyncScope();
            var handler = scope.ServiceProvider.GetRequiredService<LoggingPolicyHandler<TPolicy, TEvent>>();
            // Seed the metadata scope with the incoming event's metadata so that commands
            // dispatched by the policy inherit correlation/causation identifiers.
            await using var metadataScope = new MetadataScope(message.Metadata);
            await handler.HandleAsync(typedEvent, message.Source, ct);
        }, registration.SubscriptionId, message, cancellationToken);
    }
}
