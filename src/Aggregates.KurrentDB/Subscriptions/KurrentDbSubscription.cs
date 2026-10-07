using System.Runtime.CompilerServices;
using Aggregates.Subscriptions;
using KurrentDB.Client;

namespace Aggregates.KurrentDB;

/// <summary>
/// Adapts a KurrentDB <c>$all</c> subscription to the transport-agnostic
/// <see cref="ISubscription"/> contract. Each incoming <see cref="StreamMessage.Event"/>
/// is deserialized via <see cref="KurrentDbOptions.Deserialize"/> and wrapped in a
/// <see cref="SubscriptionMessage"/>. Non-event stream messages are skipped. A deserialization
/// failure is reported in <see cref="SubscriptionMessage.DeserializationError"/> instead of thrown.
/// </summary>
sealed class KurrentDbSubscription(Func<ValueTask> dispose, IAsyncEnumerable<StreamMessage> messages, KurrentDbOptions options) : ISubscription {

    /// <inheritdoc/>
    public IAsyncEnumerator<SubscriptionMessage> GetAsyncEnumerator(CancellationToken cancellationToken = default) =>
        EnumerateAsync(cancellationToken).GetAsyncEnumerator(cancellationToken);

    async IAsyncEnumerable<SubscriptionMessage> EnumerateAsync([EnumeratorCancellation] CancellationToken cancellationToken = default) {
        await foreach (var message in messages.WithCancellation(cancellationToken)) {
            if (message is not StreamMessage.Event eventMessage)
                continue;

            yield return ToMessage(eventMessage.ResolvedEvent.OriginalEvent);
        }
    }

    // A failing deserializer must not end the enumeration: the error travels with the message, so
    // the subscription can park it and continue with the next one.
    SubscriptionMessage ToMessage(EventRecord record) {
        var commitPosition = record.Position.CommitPosition;
        try {
            var domainEvent = options.Deserialize!(record.EventType, record.Data);
            var metadata = options.DeserializeMetadata is not null && !record.Metadata.IsEmpty
                ? options.DeserializeMetadata(record.Metadata)
                : EventMetadata.Empty;

            return new SubscriptionMessage(domainEvent, commitPosition, metadata);
        } catch (Exception exception) when (exception is not OperationCanceledException) {
            return new SubscriptionMessage(null, commitPosition, EventMetadata.Empty) { DeserializationError = exception };
        }
    }

    /// <inheritdoc/>
    public ValueTask DisposeAsync() => dispose();
}
