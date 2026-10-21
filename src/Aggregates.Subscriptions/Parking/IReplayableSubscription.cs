using System.ComponentModel;

namespace Aggregates.Subscriptions.Internal;

/// <summary>
/// A subscription whose parked messages can be replayed: it handles one message the way it does
/// while it runs, but once, without retries or parking. Implemented by the hosted services of
/// projections, policies and sagas, and registered for every subscription they run.
/// </summary>
/// <remarks>
/// Internal infrastructure of the Aggregates packages. Public only so that those packages can use it;
/// it is not covered by semantic versioning and may change in any release.
/// </remarks>
[EditorBrowsable(EditorBrowsableState.Never)]
public interface IReplayableSubscription {
    /// <summary>
    /// The subscription.
    /// </summary>
    SubscriptionRegistration Registration { get; }

    /// <summary>
    /// Handles <paramref name="message"/> once.
    /// </summary>
    /// <param name="message">The message, with an event of <see cref="SubscriptionRegistration.EventType"/>.</param>
    /// <param name="cancellationToken">A cancellation token.</param>
    /// <exception cref="InvalidOperationException">The event is not of <see cref="SubscriptionRegistration.EventType"/>.</exception>
    ValueTask HandleAsync(SubscriptionMessage message, CancellationToken cancellationToken = default);

    /// <summary>
    /// Delivers <paramref name="deadline"/> to its saga once. Only sagas receive deadlines.
    /// </summary>
    /// <param name="deadline">The deadline.</param>
    /// <param name="cancellationToken">A cancellation token.</param>
    /// <exception cref="InvalidOperationException">The subscription receives no deadlines.</exception>
    ValueTask HandleDeadlineAsync(ScheduledDeadline deadline, CancellationToken cancellationToken = default) =>
        throw new InvalidOperationException($"Subscription '{Registration.SubscriptionId}' receives no deadlines.");
}
