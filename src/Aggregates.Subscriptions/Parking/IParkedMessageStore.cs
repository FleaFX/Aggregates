namespace Aggregates.Subscriptions;

/// <summary>
/// Reads and appends the entries of parked streams, and reads back the events parked messages refer
/// to, for <see cref="ParkedMessages"/>. Implemented by the storage integrations
/// (<c>Aggregates.KurrentDB</c>, <c>Aggregates.MSSP</c>).
/// </summary>
public interface IParkedMessageStore {
    /// <summary>
    /// Reads every entry of <see cref="ParkedFormat.ParkedStream"/> of <paramref name="subscriptionId"/>,
    /// oldest first; nothing when the stream doesn't exist.
    /// </summary>
    /// <param name="subscriptionId">The id the messages were parked under.</param>
    /// <param name="cancellationToken">A cancellation token.</param>
    IAsyncEnumerable<ParkedEntry> ReadParkedAsync(string subscriptionId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Appends an entry of <paramref name="eventType"/> to <see cref="ParkedFormat.ParkedStream"/> of
    /// <paramref name="subscriptionId"/>.
    /// </summary>
    /// <param name="subscriptionId">The id the messages were parked under.</param>
    /// <param name="eventType">The event type of the entry, which starts with <c>$</c>.</param>
    /// <param name="data">The data of the entry.</param>
    /// <param name="cancellationToken">A cancellation token.</param>
    ValueTask AppendParkedAsync(string subscriptionId, string eventType, ReadOnlyMemory<byte> data, CancellationToken cancellationToken = default);

    /// <summary>
    /// Reads the event at <paramref name="source"/> as it is stored, with its metadata deserialized by
    /// the storage integration's <c>DeserializeMetadata</c>, which may throw.
    /// </summary>
    /// <param name="source">The stream and revision of the event.</param>
    /// <param name="cancellationToken">A cancellation token.</param>
    /// <returns>The event, or <see langword="null"/> when it doesn't exist (anymore).</returns>
    ValueTask<StoredEntry?> ReadEventAsync(EventReference source, CancellationToken cancellationToken = default);

    /// <summary>
    /// Deserializes an event, command or deadline payload with the storage integration's
    /// <c>Deserialize</c>; <see langword="null"/> when the type is unknown. May throw.
    /// </summary>
    /// <param name="type">The stored type name.</param>
    /// <param name="data">The serialized data.</param>
    object? Deserialize(string type, ReadOnlyMemory<byte> data);
}
