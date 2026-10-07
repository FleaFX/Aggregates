using System.Buffers.Binary;
using System.Collections.Concurrent;
using Aggregates.Subscriptions;
using KurrentDB.Client;

namespace Aggregates.KurrentDB;

/// <summary>
/// Persists subscription checkpoints in KurrentDB. Each subscription gets a dedicated stream
/// named <c>checkpoint-{subscriptionId}</c>; every call to <see cref="StoreAsync"/> appends
/// a new event containing the position as a little-endian <see cref="ulong"/>.
/// <see cref="GetAsync"/> reads the last event in that stream.
/// </summary>
/// <remarks>
/// <para>
/// Checkpoint events use the <c>$aggregates-checkpoint</c> event type. Events whose type starts
/// with <c>$</c> are never delivered to subscriptions, so storing a checkpoint does not feed
/// back into the subscriptions that produce them.
/// </para>
/// <para>
/// The first <see cref="StoreAsync"/> per subscription in this process sets <c>$maxCount</c> to 1
/// on the checkpoint stream, so only the latest checkpoint is retained.
/// </para>
/// </remarks>
public sealed class KurrentDbCheckpointStore(KurrentDBClient client) : ICheckpointStore {
    const string CheckpointEventType = "$aggregates-checkpoint";
    static readonly StreamMetadata CheckpointStreamMetadata = new(maxCount: 1);

    // Streams whose metadata has been set by this instance. Setting it is idempotent, so a
    // restart or another instance setting it again is harmless.
    readonly ConcurrentDictionary<string, byte> _streamsWithMetadata = new();

    /// <inheritdoc/>
    public async ValueTask<ulong?> GetAsync(string subscriptionId, CancellationToken cancellationToken = default) {
        var result = client.ReadStreamAsync(
            Direction.Backwards,
            StreamName(subscriptionId),
            StreamPosition.End,
            maxCount: 1,
            cancellationToken: cancellationToken);

        if (await result.ReadState == ReadState.StreamNotFound)
            return null;

        await foreach (var resolvedEvent in result.WithCancellation(cancellationToken))
            return BinaryPrimitives.ReadUInt64LittleEndian(resolvedEvent.OriginalEvent.Data.Span);

        return null;
    }

    /// <inheritdoc/>
    public async ValueTask StoreAsync(string subscriptionId, ulong position, CancellationToken cancellationToken = default) {
        Span<byte> data = stackalloc byte[sizeof(ulong)];
        BinaryPrimitives.WriteUInt64LittleEndian(data, position);

        var eventData = new EventData(Uuid.NewUuid(), CheckpointEventType, data.ToArray());
        var streamName = StreamName(subscriptionId);

        if (!_streamsWithMetadata.ContainsKey(streamName)) {
            await client.SetStreamMetadataAsync(streamName, StreamState.Any, CheckpointStreamMetadata, cancellationToken: cancellationToken);
            _streamsWithMetadata.TryAdd(streamName, 0);
        }

        await client.AppendToStreamAsync(streamName, StreamState.Any, [eventData], cancellationToken: cancellationToken);
    }

    static string StreamName(string subscriptionId) => $"checkpoint-{subscriptionId}";
}
