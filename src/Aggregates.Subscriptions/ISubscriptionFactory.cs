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
}
