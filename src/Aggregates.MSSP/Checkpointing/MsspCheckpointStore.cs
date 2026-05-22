using System.Buffers.Binary;
using Aggregates.Subscriptions;
using MSSP;

namespace Aggregates.MSSP.Checkpointing;

public sealed class MsspCheckpointStore(IMsspClient client) : ICheckpointStore {
    const string CheckpointEventType = "CheckpointStored";

    /// <inheritdoc />
    public async ValueTask<ulong?> GetAsync(string subscriptionId, CancellationToken cancellationToken = default) {
        var @event = await client.ReadAsync(StreamName(subscriptionId), cancellationToken: cancellationToken).LastOrDefaultAsync(cancellationToken: cancellationToken);

        return BinaryPrimitives.ReadUInt64LittleEndian(@event.Data.Span);
    }

    /// <inheritdoc />
    public async ValueTask StoreAsync(string subscriptionId, ulong position, CancellationToken cancellationToken = default) {
        Span<byte> data = stackalloc byte[sizeof(ulong)];
        BinaryPrimitives.WriteUInt64LittleEndian(data, position);

        var eventData = new EventData(CheckpointEventType, data.ToArray());
        await client.AppendAsync(StreamName(subscriptionId), StreamRevision.Any, [eventData], cancellationToken);
    }

    static string StreamName(string subscriptionId) => $"checkpoint-{subscriptionId}";
}
