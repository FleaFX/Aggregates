namespace Aggregates.Subscriptions;

/// <summary>
/// Creates subscriptions to an event stream. Transport-specific implementations are
/// registered by integration packages (e.g. <c>Aggregates.Sagas.KurrentDB</c>).
/// </summary>
public interface ISubscriptionFactory {
    /// <summary>
    /// Opens a subscription to the event stream, as described by <paramref name="request"/>.
    /// </summary>
    /// <param name="request">What the subscription delivers, and from where.</param>
    /// <param name="cancellationToken">
    /// A cancellation token passed to the underlying transport to cancel the subscription.
    /// </param>
    ISubscription Subscribe(SubscriptionRequest request, CancellationToken cancellationToken = default);

    /// <summary>
    /// The commit position of the last event in the store, or <see langword="null"/> when the
    /// transport can't tell (or the store is empty). Used to fix the start of a subscription
    /// that starts from the end before it subscribes. Transports that can't tell return
    /// <see langword="null"/>; the subscription then starts from the end as it subscribes.
    /// </summary>
    /// <param name="cancellationToken">A cancellation token.</param>
    ValueTask<ulong?> GetEndPositionAsync(CancellationToken cancellationToken = default) =>
        ValueTask.FromResult<ulong?>(null);

    /// <summary>
    /// Whether opening the subscription again can be expected to succeed after
    /// <paramref name="exception"/>. A subscription service subscribes again after a transient
    /// failure, and stops after any other failure.
    /// </summary>
    /// <remarks>
    /// Also used for failures of the checkpoint store and the parked-message sink, which use the
    /// same transport. Implementations should return <see langword="false"/> only for failures
    /// that need a configuration change, such as missing permissions or invalid credentials:
    /// stopping on a failure that would have passed is worse than retrying one that won't.
    /// The default treats every failure as transient.
    /// </remarks>
    /// <param name="exception">The exception that ended the subscription.</param>
    bool IsTransient(Exception exception) => true;
}
