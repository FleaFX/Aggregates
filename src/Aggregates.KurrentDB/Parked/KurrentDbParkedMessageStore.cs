using System.Runtime.CompilerServices;
using Aggregates.Subscriptions;
using KurrentDB.Client;

namespace Aggregates.KurrentDB;

/// <summary>
/// An <see cref="IParkedMessageStore"/> on KurrentDB: reads and appends the entries of
/// <c>parked-{subscriptionId}</c>, reads back the events parked messages refer to, and deserializes
/// them with <see cref="KurrentDbOptions.Deserialize"/> and <see cref="KurrentDbOptions.DeserializeMetadata"/>.
/// </summary>
/// <param name="client">The KurrentDB client.</param>
/// <param name="options">The serializers of the application.</param>
public sealed class KurrentDbParkedMessageStore(KurrentDBClient client, KurrentDbOptions options) : IParkedMessageStore {
    /// <inheritdoc/>
    public async IAsyncEnumerable<ParkedEntry> ReadParkedAsync(string subscriptionId, [EnumeratorCancellation] CancellationToken cancellationToken = default) {
        var result = client.ReadStreamAsync(Direction.Forwards, ParkedFormat.ParkedStream(subscriptionId), StreamPosition.Start, cancellationToken: cancellationToken);
        if (await result.ReadState == ReadState.StreamNotFound)
            yield break;

        await foreach (var resolved in result.WithCancellation(cancellationToken)) {
            var record = resolved.OriginalEvent;
            yield return new ParkedEntry(record.EventNumber.ToUInt64(), record.EventType, record.Data,
                new DateTimeOffset(DateTime.SpecifyKind(record.Created, DateTimeKind.Utc)));
        }
    }

    /// <inheritdoc/>
    public async ValueTask AppendParkedAsync(string subscriptionId, string eventType, ReadOnlyMemory<byte> data, CancellationToken cancellationToken = default) =>
        await client.AppendToStreamAsync(ParkedFormat.ParkedStream(subscriptionId), StreamState.Any,
            [new EventData(Uuid.NewUuid(), eventType, data)], cancellationToken: cancellationToken);

    /// <inheritdoc/>
    public async ValueTask<StoredEntry?> ReadEventAsync(EventReference source, CancellationToken cancellationToken = default) {
        try {
            var result = client.ReadStreamAsync(Direction.Forwards, source.StreamId, StreamPosition.FromInt64((long)source.Revision),
                maxCount: 1, cancellationToken: cancellationToken);
            if (await result.ReadState == ReadState.StreamNotFound)
                return null;

            await foreach (var resolved in result.WithCancellation(cancellationToken)) {
                var record = resolved.OriginalEvent;
                if (record.EventNumber.ToUInt64() != source.Revision)
                    return null;
                var metadata = options.DeserializeMetadata is not null && !record.Metadata.IsEmpty
                    ? options.DeserializeMetadata(record.Metadata)
                    : EventMetadata.Empty;
                return new StoredEntry(record.EventType, record.Data, metadata);
            }
            return null;
        } catch (StreamDeletedException) {
            return null;
        }
    }

    /// <inheritdoc/>
    public object? Deserialize(string type, ReadOnlyMemory<byte> data) => options.Deserialize!(type, data);
}
