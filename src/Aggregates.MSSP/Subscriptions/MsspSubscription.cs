using Aggregates.MSSP.Infrastructure;
using Aggregates.Subscriptions;
using MSSP;


namespace Aggregates.MSSP.Subscriptions;

/// <summary>
/// Adapts a MSSP subscription to the transport-agnostic <see cref="ISubscription"/> contract.
/// </summary>
sealed class MsspSubscription(Func<ValueTask> dispose, IAsyncEnumerable<SubscriptionEvent> messages, MsspOptions options) : ISubscription {
    /// <inheritdoc />
    public IAsyncEnumerator<SubscriptionMessage> GetAsyncEnumerator(CancellationToken cancellationToken = default) => (
        from message in messages
        select new SubscriptionMessage(message, message.Position.Value)
    ).GetAsyncEnumerator(cancellationToken);

    /// <inheritdoc />
    public ValueTask DisposeAsync() => dispose();
}
