using System.Runtime.CompilerServices;
using Aggregates.Subscriptions;
using MSSP;

namespace Aggregates.MSSP;

/// <summary>
/// Adapts a MSSP subscription to the transport-agnostic <see cref="ISubscription"/> contract.
/// Each incoming <see cref="SubscriptionEvent"/> is deserialized via <see cref="MsspOptions.Deserialize"/>
/// and wrapped in a <see cref="SubscriptionMessage"/>.
/// </summary>
/// <param name="dispose">Action to dispose the underlying subscription.</param>
/// <param name="messages">The async sequence of <see cref="SubscriptionEvent"/> messages.</param>
/// <param name="options">The <see cref="MsspOptions"/> containing deserialization configuration.</param>
sealed class MsspSubscription(Func<ValueTask> dispose, IAsyncEnumerable<SubscriptionEvent> messages, MsspOptions options) : ISubscription {
    /// <inheritdoc />
    public IAsyncEnumerator<SubscriptionMessage> GetAsyncEnumerator(CancellationToken cancellationToken = default) =>
        EnumerateAsync(cancellationToken).GetAsyncEnumerator(cancellationToken);

    async IAsyncEnumerable<SubscriptionMessage> EnumerateAsync([EnumeratorCancellation] CancellationToken cancellationToken = default) {
        await foreach (var message in messages.WithCancellation(cancellationToken)) {
            var domainEvent = options.Deserialize!(message.EventType, message.Data);
            yield return new SubscriptionMessage(domainEvent, message.Position.Value);
        }
    }

    /// <inheritdoc />
    public ValueTask DisposeAsync() => dispose();
}
