using System.Runtime.CompilerServices;
using System.Text.RegularExpressions;
using Aggregates.Subscriptions;
using MSSP;

namespace Aggregates.MSSP;

/// <summary>
/// An <see cref="IScheduledStore"/> on MSSP: reads the scheduled entries with a subscription filtered
/// on the event type prefix, evaluated by the store, appends the scheduler's entries and the requests
/// of policies, and (de)serializes deferred commands and deadline payloads with <see cref="MsspOptions.Serialize"/> and
/// <see cref="MsspOptions.Deserialize"/>. MSSP does not report progress.
/// </summary>
/// <param name="client">The MSSP client.</param>
/// <param name="options">The serializers of the application.</param>
public sealed class MsspScheduledStore(IMsspClient client, MsspOptions options) : IScheduledStore {
    static readonly SubscriptionFilter ScheduledEntries = SubscriptionFilter.ForEventTypePattern(
        new Regex("^" + Regex.Escape(ScheduledFormat.EventTypePrefix), RegexOptions.Compiled | RegexOptions.CultureInvariant));

    /// <inheritdoc/>
    public async IAsyncEnumerable<ScheduledEntry> SubscribeAsync(ulong? after, [EnumeratorCancellation] CancellationToken cancellationToken = default) {
        // MSSP starts at the given position (inclusive) and positions are consecutive.
        var from = after is { } position ? new GlobalPosition(position + 1) : GlobalPosition.Start;
        await foreach (var message in client.SubscribeAsync(ScheduledEntries, from, cancellationToken))
            yield return new ScheduledEntry(message.EventType, message.Data, Metadata(message.Metadata),
                new EventReference(message.StreamId.ToString(), message.Revision), message.Position.Value);
    }

    /// <inheritdoc/>
    public ValueTask AppendAsync(string stream, string eventType, ReadOnlyMemory<byte> data, CancellationToken cancellationToken = default) =>
        client.AppendAsync(stream, StreamRevision.Any, [new EventData(eventType, data)], cancellationToken);

    /// <inheritdoc/>
    public async ValueTask<bool> TryAppendRequestsAsync(string stream, IReadOnlyList<DeferredCommandRequest> requests, CancellationToken cancellationToken = default) {
        var metadata = options.SerializeMetadata?.Invoke(MetadataScope.Current?.Snapshot() ?? EventMetadata.Empty) ?? default;
        var events = requests.Select(request => {
            var command = options.Serialize!(request.Command);
            return new EventData(ScheduledFormat.CommandRequestEventType,
                ScheduledFormat.WriteRequest(request.Owner, request.Due, command.EventType, command.Data.Span, request.Cause), metadata);
        }).ToArray();
        try {
            await client.AppendAsync(stream, StreamRevision.NoStream, events, cancellationToken);
            return true;
        } catch (OptimisticConcurrencyException) {
            return false;
        }
    }

    /// <inheritdoc/>
    public async ValueTask<bool> ExistsAsync(string stream, CancellationToken cancellationToken = default) {
        await foreach (var _ in client.ReadAsync(stream, maxCount: 1, cancellationToken: cancellationToken))
            return true;
        return false;
    }

    /// <inheritdoc/>
    public object? Deserialize(string type, ReadOnlyMemory<byte> data) => options.Deserialize!(type, data);

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
