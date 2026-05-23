using System.Buffers.Binary;
using Aggregates.Subscriptions;
using MSSP;

namespace Aggregates.MSSP;

/// <summary>
/// An <see cref="ICheckpointStore"/> implementation that stores checkpoint positions in MSSP.
/// Each subscription gets its own stream in the format <c>checkpoint-{subscriptionId}</c>.
/// </summary>
public sealed class MsspCheckpointStore(IMsspClient client) : ICheckpointStore {
    const string CheckpointEventType = "CheckpointStored";

    /// <inheritdoc />
    public async ValueTask<ulong?> GetAsync(string subscriptionId, CancellationToken cancellationToken = default) {
        var result = client.ReadAsync(
            StreamName(subscriptionId),
            direction: ReadDirection.Backwards,
            maxCount: 1,
            cancellationToken: cancellationToken
        );

        await foreach (var resolvedEvent in result.WithCancellation(cancellationToken))
            return BinaryPrimitives.ReadUInt64LittleEndian(resolvedEvent.Data.Span);

        return null;
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
