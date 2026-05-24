using System.Runtime.CompilerServices;
using Aggregates.Subscriptions;
using KurrentDB.Client;

namespace Aggregates.KurrentDB;

/// <summary>
/// Adapts a KurrentDB <c>$all</c> subscription to the transport-agnostic
/// <see cref="ISubscription"/> contract. Each incoming <see cref="StreamMessage.Event"/>
/// is deserialized via <see cref="KurrentDbOptions.Deserialize"/> and wrapped in a
/// <see cref="SubscriptionMessage"/>. Non-event stream messages are skipped.
/// </summary>
sealed class KurrentDbSubscription(Func<ValueTask> dispose, IAsyncEnumerable<StreamMessage> messages, KurrentDbOptions options) : ISubscription {

    /// <inheritdoc/>
    public IAsyncEnumerator<SubscriptionMessage> GetAsyncEnumerator(CancellationToken cancellationToken = default) =>
        EnumerateAsync(cancellationToken).GetAsyncEnumerator(cancellationToken);

    async IAsyncEnumerable<SubscriptionMessage> EnumerateAsync([EnumeratorCancellation] CancellationToken cancellationToken = default) {
        await foreach (var message in messages.WithCancellation(cancellationToken)) {
            if (message is not StreamMessage.Event eventMessage)
                continue;

            var resolvedEvent = eventMessage.ResolvedEvent;
            var commitPosition = resolvedEvent.OriginalEvent.Position.CommitPosition;
            var domainEvent = options.Deserialize!(
                resolvedEvent.OriginalEvent.EventType,
                resolvedEvent.OriginalEvent.Data);

            var rawMetadata = resolvedEvent.OriginalEvent.Metadata;
            var metadata = options.DeserializeMetadata is not null && !rawMetadata.IsEmpty
                ? options.DeserializeMetadata(rawMetadata)
                : EventMetadata.Empty;

            yield return new SubscriptionMessage(domainEvent, commitPosition, metadata);
        }
    }

    /// <inheritdoc/>
    public ValueTask DisposeAsync() => dispose();
}
