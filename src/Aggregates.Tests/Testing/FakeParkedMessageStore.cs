using Aggregates.Subscriptions;

namespace Aggregates.Testing;

/// <summary>
/// An in-memory <see cref="IParkedMessageStore"/>: parked streams as lists, stored events by reference,
/// and a configurable deserializer.
/// </summary>
sealed class FakeParkedMessageStore : IParkedMessageStore {
    readonly Dictionary<string, List<ParkedEntry>> _streams = [];

    /// <summary>
    /// The events <see cref="ReadEventAsync"/> finds, by reference.
    /// </summary>
    public Dictionary<EventReference, StoredEntry> Events { get; } = [];

    /// <summary>
    /// Deserializes stored data; returns <see langword="null"/> unless a test sets it.
    /// </summary>
    public Func<string, ReadOnlyMemory<byte>, object?> Deserializer { get; set; } = (_, _) => null;

    /// <summary>
    /// Parks <paramref name="message"/> under <paramref name="subscriptionId"/> as a sink would.
    /// </summary>
    /// <returns>The revision of the parked message.</returns>
    public ulong Park(string subscriptionId, SubscriptionMessage message, Exception exception) =>
        Append(subscriptionId, ParkedFormat.ParkedEventType, ParkedFormat.WriteParked(message, exception));

    /// <summary>
    /// Appends an entry with any data, for invalid or closing entries.
    /// </summary>
    /// <returns>The revision of the entry.</returns>
    public ulong Append(string subscriptionId, string eventType, ReadOnlyMemory<byte> data) {
        lock (_streams) {
            if (!_streams.TryGetValue(subscriptionId, out var stream))
                _streams[subscriptionId] = stream = [];
            var revision = (ulong)stream.Count;
            stream.Add(new ParkedEntry(revision, eventType, data, new DateTimeOffset(2026, 10, 15, 8, 0, 0, TimeSpan.Zero)));
            return revision;
        }
    }

    /// <summary>
    /// The entries of the parked stream of <paramref name="subscriptionId"/>, in order.
    /// </summary>
    public IReadOnlyList<ParkedEntry> Entries(string subscriptionId) {
        lock (_streams)
            return _streams.TryGetValue(subscriptionId, out var stream) ? [.. stream] : [];
    }

    /// <inheritdoc/>
    public IAsyncEnumerable<ParkedEntry> ReadParkedAsync(string subscriptionId, CancellationToken cancellationToken = default) =>
        Entries(subscriptionId).ToAsyncEnumerable();

    /// <inheritdoc/>
    public ValueTask AppendParkedAsync(string subscriptionId, string eventType, ReadOnlyMemory<byte> data, CancellationToken cancellationToken = default) {
        Append(subscriptionId, eventType, data);
        return ValueTask.CompletedTask;
    }

    /// <inheritdoc/>
    public ValueTask<StoredEntry?> ReadEventAsync(EventReference source, CancellationToken cancellationToken = default) =>
        ValueTask.FromResult(Events.TryGetValue(source, out var entry) ? entry : (StoredEntry?)null);

    /// <inheritdoc/>
    public object? Deserialize(string type, ReadOnlyMemory<byte> data) => Deserializer(type, data);
}
