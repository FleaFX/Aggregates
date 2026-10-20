namespace Aggregates.Subscriptions;

/// <summary>
/// The state of a subscription or a scheduler, as reported to the <see cref="SubscriptionMonitor"/>.
/// </summary>
public enum SubscriptionState {
    /// <summary>
    /// The subscription handles messages, or the scheduler reads and executes its requests. A
    /// subscription starts in this state.
    /// </summary>
    Running,

    /// <summary>
    /// Only a scheduler: its handler never deferred a command or requested a deadline, so it doesn't
    /// read the store yet. A scheduler starts in this state.
    /// </summary>
    Waiting,

    /// <summary>
    /// A transient failure (see <see cref="ISubscriptionFactory.IsTransient"/>), or a subscription that
    /// ended unexpectedly: it tries again with backoff. A subscription is running again once a message
    /// arrives after the failure, so on a quiet store it stays retrying until the next event; a
    /// scheduler once the failed action succeeds or its feed reads an entry.
    /// </summary>
    Retrying,

    /// <summary>
    /// Only a subscription that blocks (<see cref="SubscriptionErrorMode.Block"/>): a message still fails
    /// after <see cref="SubscriptionErrorHandlingOptions.MaxRetries"/> attempts, or can't be deserialized,
    /// and the subscription handles no later message until it succeeds.
    /// </summary>
    Blocked,

    /// <summary>
    /// The subscription or scheduler ended: after a failure that is not transient (see
    /// <see cref="SubscriptionStatus.Error"/>), because the other half of its hosted service ended, or
    /// because the host stops. It doesn't start again in this host.
    /// </summary>
    Stopped
}
