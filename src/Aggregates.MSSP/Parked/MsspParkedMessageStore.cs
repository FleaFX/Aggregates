using System.Runtime.CompilerServices;
using Aggregates.Subscriptions;
using MSSP;

namespace Aggregates.MSSP;

/// <summary>
/// An <see cref="IParkedMessageStore"/> on MSSP: reads and appends the entries of
/// <c>parked-{subscriptionId}</c>, reads back the events parked messages refer to, and deserializes
/// them with <see cref="MsspOptions.Deserialize"/> and <see cref="MsspOptions.DeserializeMetadata"/>.
/// </summary>
/// <param name="client">The MSSP client.</param>
/// <param name="options">The serializers of the application.</param>
public sealed class MsspParkedMessageStore(IMsspClient client, MsspOptions options) : IParkedMessageStore {
    /// <inheritdoc/>
    public async IAsyncEnumerable<ParkedEntry> ReadParkedAsync(string subscriptionId, [EnumeratorCancellation] CancellationToken cancellationToken = default) {
        await foreach (var recorded in client.ReadAsync(ParkedFormat.ParkedStream(subscriptionId), cancellationToken: cancellationToken))
            yield return new ParkedEntry(recorded.Revision, recorded.EventType, recorded.Data, recorded.Timestamp);
    }

    /// <inheritdoc/>
    public ValueTask AppendParkedAsync(string subscriptionId, string eventType, ReadOnlyMemory<byte> data, CancellationToken cancellationToken = default) =>
        client.AppendAsync(ParkedFormat.ParkedStream(subscriptionId), StreamRevision.Any, [new EventData(eventType, data)], cancellationToken);

    /// <inheritdoc/>
    public async ValueTask<StoredEntry?> ReadEventAsync(EventReference source, CancellationToken cancellationToken = default) {
        await foreach (var recorded in client.ReadAsync(source.StreamId, source.Revision, maxCount: 1, cancellationToken: cancellationToken)) {
            if (recorded.Revision != source.Revision)
                return null;
            var metadata = options.DeserializeMetadata is not null && !recorded.Metadata.IsEmpty
                ? options.DeserializeMetadata(recorded.Metadata)
                : EventMetadata.Empty;
            return new StoredEntry(recorded.EventType, recorded.Data, metadata);
        }
        return null;
    }

    /// <inheritdoc/>
    public object? Deserialize(string type, ReadOnlyMemory<byte> data) => options.Deserialize!(type, data);
}
