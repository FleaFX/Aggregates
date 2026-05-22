using Aggregates.Subscriptions;
using MSSP;


namespace Aggregates.MSSP;

/// <summary>
/// Adapts a MSSP subscription to the transport-agnostic <see cref="ISubscription"/> contract.
/// </summary>
/// <param name="dispose">Action to dispose the underlying subscription.</param>
/// <param name="messages">The async sequence of <see cref="SubscriptionEvent"/> messages.</param>
/// <param name="options">The <see cref="MsspOptions"/> containing deserialization configuration.</param>
sealed class MsspSubscription(Func<ValueTask> dispose, IAsyncEnumerable<SubscriptionEvent> messages, MsspOptions options) : ISubscription {
    /// <inheritdoc />
    public IAsyncEnumerator<SubscriptionMessage> GetAsyncEnumerator(CancellationToken cancellationToken = default) => (
        from message in messages
        select new SubscriptionMessage(message, message.Position.Value)
    ).GetAsyncEnumerator(cancellationToken);

    /// <inheritdoc />
    public ValueTask DisposeAsync() => dispose();
}
