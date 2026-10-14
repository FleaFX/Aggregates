using System.Runtime.CompilerServices;
using System.Text.RegularExpressions;
using Aggregates.MSSP;
using Aggregates.Subscriptions;
using MSSP;

namespace Aggregates.Sagas.MSSP;

/// <summary>
/// Reads the timeout entries of saga streams with a subscription filtered on the event type prefix,
/// evaluated by the store, appends the scheduler's entries, and deserializes planned commands with
/// <see cref="MsspOptions.Deserialize"/>. MSSP does not report progress.
/// </summary>
sealed class MsspSagaTimeoutStore(IMsspClient client, MsspOptions options) : ISagaTimeoutStore {
    static readonly SubscriptionFilter TimeoutEntries = SubscriptionFilter.ForEventTypePattern(
        new Regex("^" + Regex.Escape(SagaTimeoutFormat.EventTypePrefix), RegexOptions.Compiled | RegexOptions.CultureInvariant));

    /// <inheritdoc/>
    public async IAsyncEnumerable<SagaTimeoutEntry> SubscribeAsync(ulong? after, [EnumeratorCancellation] CancellationToken cancellationToken = default) {
        // MSSP starts at the given position (inclusive) and positions are consecutive.
        var from = after is { } position ? new GlobalPosition(position + 1) : GlobalPosition.Start;
        await foreach (var message in client.SubscribeAsync(TimeoutEntries, from, cancellationToken))
            yield return new SagaTimeoutEntry(message.EventType, message.Data, Metadata(message.Metadata),
                new EventReference(message.StreamId.ToString(), message.Revision), message.Position.Value);
    }

    /// <inheritdoc/>
    public ValueTask AppendAsync(string stream, string eventType, ReadOnlyMemory<byte> data, CancellationToken cancellationToken = default) =>
        client.AppendAsync(stream, StreamRevision.Any, [new EventData(eventType, data)], cancellationToken);

    /// <inheritdoc/>
    public async ValueTask<bool> ExistsAsync(string stream, CancellationToken cancellationToken = default) {
        await foreach (var _ in client.ReadAsync(stream, maxCount: 1, cancellationToken: cancellationToken))
            return true;
        return false;
    }

    /// <inheritdoc/>
    public object? DeserializeCommand(string commandType, ReadOnlyMemory<byte> data) => options.Deserialize!(commandType, data);

    /// <summary>
    /// Deserializes stored metadata, or returns empty metadata when that fails.
    /// </summary>
    // A failing metadata deserializer must not stop the scheduler: the command runs without the request's metadata.
    EventMetadata Metadata(ReadOnlyMemory<byte> metadata) {
        if (options.DeserializeMetadata is null || metadata.IsEmpty)
            return EventMetadata.Empty;
        try {
            return options.DeserializeMetadata(metadata);
        } catch (Exception exception) when (exception is not OperationCanceledException) {
            return EventMetadata.Empty;
        }
    }
}
