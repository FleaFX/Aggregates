using System.Runtime.CompilerServices;
using Aggregates.Subscriptions;
using KurrentDB.Client;

namespace Aggregates.KurrentDB;

/// <summary>
/// An <see cref="IScheduledStore"/> on KurrentDB: reads the scheduled entries from <c>$all</c> with a
/// server-side prefix filter, appends the scheduler's entries and the requests of policies, and
/// (de)serializes deferred commands with <see cref="KurrentDbOptions.Serialize"/> and
/// <see cref="KurrentDbOptions.Deserialize"/>.
/// </summary>
/// <param name="client">The KurrentDB client.</param>
/// <param name="options">The serializers of the application.</param>
public sealed class KurrentDbScheduledStore(KurrentDBClient client, KurrentDbOptions options) : IScheduledStore {
    // The server reports progress once per 32 x 32 events it reads (spike v1-05).
    const uint CheckpointInterval = 32;

    /// <inheritdoc/>
    public async IAsyncEnumerable<ScheduledEntry> SubscribeAsync(ulong? after, [EnumeratorCancellation] CancellationToken cancellationToken = default) {
        var from = after is { } position ? FromAll.After(new Position(position, position)) : FromAll.Start;
        var filterOptions = new SubscriptionFilterOptions(EventTypeFilter.Prefix(ScheduledFormat.EventTypePrefix), CheckpointInterval);
        await using var subscription = client.SubscribeToAll(from, filterOptions: filterOptions, cancellationToken: cancellationToken);
        await foreach (var message in subscription.Messages.WithCancellation(cancellationToken)) {
            switch (message) {
                case StreamMessage.Event eventMessage:
                    var record = eventMessage.ResolvedEvent.OriginalEvent;
                    yield return new ScheduledEntry(record.EventType, record.Data, Metadata(record.Metadata),
                        new EventReference(record.EventStreamId, record.EventNumber.ToUInt64()), record.Position.CommitPosition);
                    break;
                case StreamMessage.AllStreamCheckpointReached checkpoint:
                    yield return ScheduledEntry.Progress(checkpoint.Position.CommitPosition);
                    break;
            }
        }
    }

    /// <inheritdoc/>
    public async ValueTask AppendAsync(string stream, string eventType, ReadOnlyMemory<byte> data, CancellationToken cancellationToken = default) =>
        await client.AppendToStreamAsync(stream, StreamState.Any, [new EventData(Uuid.NewUuid(), eventType, data)], cancellationToken: cancellationToken);

    /// <inheritdoc/>
    public async ValueTask<bool> TryAppendRequestsAsync(string stream, IReadOnlyList<DeferredCommandRequest> requests, CancellationToken cancellationToken = default) {
        var metadata = options.SerializeMetadata?.Invoke(MetadataScope.Current?.Snapshot() ?? EventMetadata.Empty);
        var events = requests.Select(request => {
            var command = options.Serialize!(request.Command);
            return new EventData(Uuid.NewUuid(), ScheduledFormat.CommandRequestEventType,
                ScheduledFormat.WriteCommandRequest(request.Owner, request.Due, command.EventType, command.Data.Span), metadata);
        }).ToArray();
        try {
            await client.AppendToStreamAsync(stream, StreamState.NoStream, events, cancellationToken: cancellationToken);
            return true;
        } catch (WrongExpectedVersionException) {
            return false;
        }
    }

    /// <inheritdoc/>
    public async ValueTask<bool> ExistsAsync(string stream, CancellationToken cancellationToken = default) {
        var result = client.ReadStreamAsync(Direction.Backwards, stream, StreamPosition.End, maxCount: 1, cancellationToken: cancellationToken);
        return await result.ReadState != ReadState.StreamNotFound;
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
