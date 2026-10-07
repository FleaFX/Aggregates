namespace Aggregates.Subscriptions;

/// <summary>
/// Creates subscriptions to an event stream. Transport-specific implementations are
/// registered by integration packages (e.g. <c>Aggregates.Sagas.KurrentDB</c>).
/// </summary>
public interface ISubscriptionFactory {
    /// <summary>
    /// Opens a subscription to the event stream.
    /// </summary>
    /// <param name="fromPosition">
    /// The exclusive starting position, or <see langword="null"/> to start from the beginning
    /// (unless <paramref name="startFromEnd"/> overrides this).
    /// </param>
    /// <param name="startFromEnd">
    /// When <see langword="true"/> and <paramref name="fromPosition"/> is <see langword="null"/>,
    /// starts from the current end of the stream rather than the beginning.
    /// </param>
    /// <param name="cancellationToken">
    /// A cancellation token passed to the underlying transport to cancel the subscription.
    /// </param>
    ISubscription Subscribe(ulong? fromPosition, bool startFromEnd, CancellationToken cancellationToken = default);

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
