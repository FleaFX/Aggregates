using System.Threading.Channels;
using Aggregates.Sagas;
using Aggregates.Subscriptions;

namespace Aggregates.Testing;

/// <summary>
/// An in-memory <see cref="ISagaTimeoutStore"/>: entries get consecutive positions from 1, and a
/// subscription yields the stored entries after its position, then every entry appended later.
/// </summary>
public sealed class FakeSagaTimeoutStore : ISagaTimeoutStore {
    readonly object _lock = new();
    readonly List<SagaTimeoutEntry> _entries = [];
    readonly List<Channel<SagaTimeoutEntry>> _subscribers = [];

    /// <summary>
    /// The number of <see cref="SubscribeAsync"/> calls so far.
    /// </summary>
    public int Subscriptions { get; private set; }

    /// <summary>
    /// The exception the next <see cref="SubscribeAsync"/> throws; reset after it does.
    /// </summary>
    public Exception? FailNextSubscribe { get; set; }

    /// <summary>
    /// Deserializes a planned command; returns null for an unknown type by default.
    /// </summary>
    public Func<string, ReadOnlyMemory<byte>, object?> Deserializer { get; set; } = (_, _) => null;

    /// <summary>
    /// A snapshot of the stored entries, in order.
    /// </summary>
    public IReadOnlyList<SagaTimeoutEntry> Entries {
        get {
            lock (_lock)
                return [.. _entries];
        }
    }

    /// <summary>
    /// Appends an entry and returns it: positions are consecutive from 1, and a revision counts
    /// the entries of its stream.
    /// </summary>
    public SagaTimeoutEntry Append(string stream, string eventType, ReadOnlyMemory<byte> data, EventMetadata? metadata = null) {
        lock (_lock) {
            var position = (ulong)(_entries.Count + 1);
            var revision = (ulong)_entries.Count(entry => entry.Source.StreamId == stream);
            var entry = new SagaTimeoutEntry(eventType, data, metadata ?? EventMetadata.Empty, new EventReference(stream, revision), position);
            _entries.Add(entry);
            foreach (var subscriber in _subscribers)
                subscriber.Writer.TryWrite(entry);
            return entry;
        }
    }

    /// <summary>
    /// Appends a timeout request of <paramref name="owner"/> to the saga stream <paramref name="sagaId"/>.
    /// </summary>
    public SagaTimeoutEntry AppendRequest(string sagaId, string owner, DateTimeOffset due, string commandType = "Test.Command@v1", EventMetadata? metadata = null) =>
        Append(sagaId, SagaTimeoutFormat.RequestEventType, SagaTimeoutFormat.WriteRequest(owner, due, commandType, "{}"u8), metadata);

    /// <summary>
    /// Appends the entry that records <paramref name="request"/> as delivered.
    /// </summary>
    public SagaTimeoutEntry AppendDelivery(string schedulerStream, EventReference request) =>
        Append(schedulerStream, SagaTimeoutFormat.DeliveryEventType, request.ToLinkData());

    /// <inheritdoc/>
    public async IAsyncEnumerable<SagaTimeoutEntry> SubscribeAsync(ulong? after, [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken cancellationToken = default) {
        Subscriptions++;
        if (FailNextSubscribe is { } failure) {
            FailNextSubscribe = null;
            throw failure;
        }

        IReadOnlyList<SagaTimeoutEntry> snapshot;
        Channel<SagaTimeoutEntry> channel;
        lock (_lock) {
            snapshot = [.. _entries.Where(entry => entry.CommitPosition > (after ?? 0))];
            channel = Channel.CreateUnbounded<SagaTimeoutEntry>(new UnboundedChannelOptions { SingleReader = true });
            _subscribers.Add(channel);
        }

        foreach (var entry in snapshot)
            yield return entry;
        await foreach (var entry in channel.Reader.ReadAllAsync(cancellationToken))
            yield return entry;
    }

    /// <inheritdoc/>
    public ValueTask AppendAsync(string stream, string eventType, ReadOnlyMemory<byte> data, CancellationToken cancellationToken = default) {
        Append(stream, eventType, data);
        return ValueTask.CompletedTask;
    }

    /// <inheritdoc/>
    public ValueTask<bool> ExistsAsync(string stream, CancellationToken cancellationToken = default) =>
        ValueTask.FromResult(Entries.Any(entry => entry.Source.StreamId == stream));

    /// <inheritdoc/>
    public object? DeserializeCommand(string commandType, ReadOnlyMemory<byte> data) =>
        Deserializer(commandType, data);
}
