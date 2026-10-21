using Aggregates.Subscriptions;
using Aggregates.Subscriptions.Internal;
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
/// While a live event is projected, <see cref="ProjectedEvent.Current"/> describes it.
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

        await ProjectAsync(typedEvent, message.Metadata, position: null, cancellationToken);
    }

    /// <inheritdoc/>
    protected override Task ExecuteAsync(CancellationToken stoppingToken) =>
        loop.RunAsync(registration, ProcessAsync, stoppingToken);

    async ValueTask ProcessAsync(SubscriptionMessage message, CancellationToken cancellationToken) {
        if (message.Event is not TEvent typedEvent)
            return;

        await retryPolicy.ExecuteAsync(ct => ProjectAsync(typedEvent, message.Metadata, message.CommitPosition, ct),
            registration.SubscriptionId, message, registration.ErrorMode, cancellationToken);
    }

    /// <summary>
    /// Projects <paramref name="event"/> once, in a fresh scope. For a live event (with a
    /// <paramref name="position"/>), <see cref="ProjectedEvent.Current"/> describes it during the
    /// projection and its commit; a replayed event has none.
    /// </summary>
    async ValueTask ProjectAsync(TEvent @event, EventMetadata metadata, ulong? position, CancellationToken cancellationToken) {
        await using var scope = scopeFactory.CreateAsyncScope();
        // Set inside this async method: the value flows into the commit and is restored when the method returns.
        // A new instance per attempt, so a retry numbers its transactions from 1 again.
        ProjectedEvent.Current = position is { } livePosition
            ? new ProjectedEvent(registration.SubscriptionId, livePosition, scope.ServiceProvider)
            : null;
        var handler = scope.ServiceProvider.GetRequiredService<LoggingProjectionHandler<TProjection, TEvent>>();
        await handler.HandleAsync(@event, metadata, cancellationToken);
    }
}
