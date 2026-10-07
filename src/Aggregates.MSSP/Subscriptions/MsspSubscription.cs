using System.Runtime.CompilerServices;
using Aggregates.Subscriptions;
using MSSP;

namespace Aggregates.MSSP;

/// <summary>
/// Adapts a MSSP subscription to the transport-agnostic <see cref="ISubscription"/> contract.
/// Each incoming <see cref="SubscriptionEvent"/> is deserialized via <see cref="MsspOptions.Deserialize"/>
/// and wrapped in a <see cref="SubscriptionMessage"/>. A deserialization failure is reported in
/// <see cref="SubscriptionMessage.DeserializationError"/> instead of thrown.
/// </summary>
/// <param name="dispose">Action to dispose the underlying subscription.</param>
/// <param name="messages">The async sequence of <see cref="SubscriptionEvent"/> messages.</param>
/// <param name="options">The <see cref="MsspOptions"/> containing deserialization configuration.</param>
sealed class MsspSubscription(Func<ValueTask> dispose, IAsyncEnumerable<SubscriptionEvent> messages, MsspOptions options) : ISubscription {
    /// <inheritdoc />
    public IAsyncEnumerator<SubscriptionMessage> GetAsyncEnumerator(CancellationToken cancellationToken = default) =>
        EnumerateAsync(cancellationToken).GetAsyncEnumerator(cancellationToken);

    async IAsyncEnumerable<SubscriptionMessage> EnumerateAsync([EnumeratorCancellation] CancellationToken cancellationToken = default) {
        await foreach (var message in messages.WithCancellation(cancellationToken))
            yield return ToMessage(message);
    }

    // A failing deserializer must not end the enumeration: the error travels with the message, so
    // the subscription can park it and continue with the next one.
    SubscriptionMessage ToMessage(SubscriptionEvent message) {
        try {
            var domainEvent = options.Deserialize!(message.EventType, message.Data);
            var metadata = options.DeserializeMetadata is not null && !message.Metadata.IsEmpty
                ? options.DeserializeMetadata(message.Metadata)
                : EventMetadata.Empty;
            return new SubscriptionMessage(domainEvent, message.Position.Value, metadata);
        } catch (Exception exception) when (exception is not OperationCanceledException) {
            return new SubscriptionMessage(null, message.Position.Value, EventMetadata.Empty) { DeserializationError = exception };
        }
    }

    /// <inheritdoc />
    public ValueTask DisposeAsync() => dispose();
}
