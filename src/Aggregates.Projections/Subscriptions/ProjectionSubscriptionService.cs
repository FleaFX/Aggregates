using Aggregates.Subscriptions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace Aggregates.Projections;

/// <summary>
/// A hosted service that subscribes to an event stream and routes incoming events to
/// <typeparamref name="TProjection"/>.
/// </summary>
/// <remarks>
/// The subscription runs in a <see cref="SubscriptionLoop"/>, which subscribes again after a
/// transient failure, parks messages that could not be deserialized, and records checkpoints.
/// For each event that matches <typeparamref name="TEvent"/>, the service resolves the handler
/// chain of <typeparamref name="TProjection"/> and projects the event, with automatic retry and
/// parked-message fallback via <see cref="SubscriptionRetryPolicy"/>, or retries until it succeeds
/// when the contract blocks (<see cref="SubscriptionErrorMode.Block"/>).
/// A fresh DI scope is created per event so that scoped services are never captured as
/// singletons by this long-lived hosted service.
/// As an <see cref="IReplayableSubscription"/>, it projects a replayed parked event the same way, once.
/// </remarks>
/// <typeparam name="TProjection">The projection class.</typeparam>
/// <typeparam name="TEvent">The event type the projection handles.</typeparam>
sealed class ProjectionSubscriptionService<TProjection, TEvent>(
    SubscriptionLoop loop,
    IServiceScopeFactory scopeFactory,
    SubscriptionRetryPolicy retryPolicy,
    SubscriptionRegistration registration) : BackgroundService, IReplayableSubscription
    where TProjection : IProjection<TEvent> {

    /// <inheritdoc/>
    public SubscriptionRegistration Registration => registration;

    /// <inheritdoc/>
    public async ValueTask HandleAsync(SubscriptionMessage message, CancellationToken cancellationToken = default) {
        if (message.Event is not TEvent typedEvent)
            throw new InvalidOperationException(
                $"Subscription '{registration.SubscriptionId}' handles {typeof(TEvent).Name}, but the event of {message.Source} is a {message.Event?.GetType().Name ?? "null"}.");

        await ProjectAsync(typedEvent, message.Metadata, cancellationToken);
    }

    /// <inheritdoc/>
    protected override Task ExecuteAsync(CancellationToken stoppingToken) =>
        loop.RunAsync(registration, ProcessAsync, stoppingToken);

    async ValueTask ProcessAsync(SubscriptionMessage message, CancellationToken cancellationToken) {
        if (message.Event is not TEvent typedEvent)
            return;

        await retryPolicy.ExecuteAsync(ct => ProjectAsync(typedEvent, message.Metadata, ct),
            registration.SubscriptionId, message, registration.ErrorMode, cancellationToken);
    }

    /// <summary>
    /// Projects <paramref name="event"/> once, in a fresh scope.
    /// </summary>
    async ValueTask ProjectAsync(TEvent @event, EventMetadata metadata, CancellationToken cancellationToken) {
        await using var scope = scopeFactory.CreateAsyncScope();
        var handler = scope.ServiceProvider.GetRequiredService<LoggingProjectionHandler<TProjection, TEvent>>();
        await handler.HandleAsync(@event, metadata, cancellationToken);
    }
}
