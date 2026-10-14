using System.Runtime.CompilerServices;
using Aggregates.KurrentDB;
using Aggregates.Subscriptions;
using KurrentDB.Client;

namespace Aggregates.Sagas.KurrentDB;

/// <summary>
/// Reads the timeout entries of saga streams from <c>$all</c>, with a server-side prefix filter,
/// appends the scheduler's entries, and deserializes planned commands with
/// <see cref="KurrentDbOptions.Deserialize"/>.
/// </summary>
sealed class KurrentDbSagaTimeoutStore(KurrentDBClient client, KurrentDbOptions options) : ISagaTimeoutStore {
    // The server reports progress once per 32 x 32 events it reads (spike v1-05).
    const uint CheckpointInterval = 32;

    /// <inheritdoc/>
    public async IAsyncEnumerable<SagaTimeoutEntry> SubscribeAsync(ulong? after, [EnumeratorCancellation] CancellationToken cancellationToken = default) {
        var from = after is { } position ? FromAll.After(new Position(position, position)) : FromAll.Start;
        var filterOptions = new SubscriptionFilterOptions(EventTypeFilter.Prefix(SagaTimeoutFormat.EventTypePrefix), CheckpointInterval);
        await using var subscription = client.SubscribeToAll(from, filterOptions: filterOptions, cancellationToken: cancellationToken);
        await foreach (var message in subscription.Messages.WithCancellation(cancellationToken)) {
            switch (message) {
                case StreamMessage.Event eventMessage:
                    var record = eventMessage.ResolvedEvent.OriginalEvent;
                    yield return new SagaTimeoutEntry(record.EventType, record.Data, Metadata(record.Metadata),
                        new EventReference(record.EventStreamId, record.EventNumber.ToUInt64()), record.Position.CommitPosition);
                    break;
                case StreamMessage.AllStreamCheckpointReached checkpoint:
                    yield return SagaTimeoutEntry.Progress(checkpoint.Position.CommitPosition);
                    break;
            }
        }
    }

    /// <inheritdoc/>
    public async ValueTask AppendAsync(string stream, string eventType, ReadOnlyMemory<byte> data, CancellationToken cancellationToken = default) =>
        await client.AppendToStreamAsync(stream, StreamState.Any, [new EventData(Uuid.NewUuid(), eventType, data)], cancellationToken: cancellationToken);

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
