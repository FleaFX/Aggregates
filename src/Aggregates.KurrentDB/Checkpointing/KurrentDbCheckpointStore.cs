using System.Buffers.Binary;
using Aggregates.Subscriptions;
using KurrentDB.Client;

namespace Aggregates.KurrentDB;

/// <summary>
/// Persists subscription checkpoints in KurrentDB. Each subscription gets a dedicated stream
/// named <c>checkpoint-{subscriptionId}</c>; every call to <see cref="StoreAsync"/> appends
/// a new event containing the position as a little-endian <see cref="ulong"/>.
/// <see cref="GetAsync"/> reads the last event in that stream.
/// </summary>
public sealed class KurrentDbCheckpointStore(KurrentDBClient client) : ICheckpointStore {
    const string CheckpointEventType = "CheckpointStored";

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
        await client.AppendToStreamAsync(StreamName(subscriptionId), StreamState.Any, [eventData], cancellationToken: cancellationToken);
    }

    static string StreamName(string subscriptionId) => $"checkpoint-{subscriptionId}";
}
