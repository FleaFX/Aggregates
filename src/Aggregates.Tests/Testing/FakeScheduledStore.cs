using System.Threading.Channels;
using Aggregates.Sagas;
using Aggregates.Subscriptions;

namespace Aggregates.Testing;

/// <summary>
/// An in-memory <see cref="IScheduledStore"/>: entries get consecutive positions from 1, and a
/// subscription yields the stored entries after its position, then every entry appended later.
/// </summary>
public sealed class FakeScheduledStore : IScheduledStore {
    readonly object _lock = new();
    readonly List<ScheduledEntry> _entries = [];
    readonly List<Channel<ScheduledEntry>> _subscribers = [];

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
    public IReadOnlyList<ScheduledEntry> Entries {
        get {
            lock (_lock)
                return [.. _entries];
        }
    }

    /// <summary>
    /// Appends an entry and returns it: positions are consecutive from 1, and a revision counts
    /// the entries of its stream.
    /// </summary>
    public ScheduledEntry Append(string stream, string eventType, ReadOnlyMemory<byte> data, EventMetadata? metadata = null) {
        lock (_lock) {
            var position = (ulong)(_entries.Count + 1);
            var revision = (ulong)_entries.Count(entry => entry.Source.StreamId == stream);
            var entry = new ScheduledEntry(eventType, data, metadata ?? EventMetadata.Empty, new EventReference(stream, revision), position);
            _entries.Add(entry);
            foreach (var subscriber in _subscribers)
                subscriber.Writer.TryWrite(entry);
            return entry;
        }
    }

    /// <summary>
    /// Appends a deferred command request of <paramref name="owner"/> to the stream <paramref name="sagaId"/>.
    /// </summary>
    public ScheduledEntry AppendRequest(string sagaId, string owner, DateTimeOffset due, string commandType = "Test.Command@v1", EventMetadata? metadata = null) =>
        Append(sagaId, ScheduledFormat.CommandRequestEventType, ScheduledFormat.WriteCommandRequest(owner, due, commandType, "{}"u8), metadata);

    /// <summary>
    /// Appends the entry that records <paramref name="request"/> as delivered.
    /// </summary>
    public ScheduledEntry AppendDelivery(string schedulerStream, EventReference request) =>
        Append(schedulerStream, ScheduledFormat.DeliveryEventType, request.ToLinkData());

    /// <inheritdoc/>
    public async IAsyncEnumerable<ScheduledEntry> SubscribeAsync(ulong? after, [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken cancellationToken = default) {
        Subscriptions++;
        if (FailNextSubscribe is { } failure) {
            FailNextSubscribe = null;
            throw failure;
        }

        IReadOnlyList<ScheduledEntry> snapshot;
        Channel<ScheduledEntry> channel;
        lock (_lock) {
            snapshot = [.. _entries.Where(entry => entry.CommitPosition > (after ?? 0))];
            channel = Channel.CreateUnbounded<ScheduledEntry>(new UnboundedChannelOptions { SingleReader = true });
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

    /// <summary>
    /// The stored type name of a deferred command; its full type name by default.
    /// </summary>
    public Func<ICommand, string> CommandType { get; set; } = command => command.GetType().FullName!;

    /// <inheritdoc/>
    public ValueTask<bool> TryAppendRequestsAsync(string stream, IReadOnlyList<DeferredCommandRequest> requests, CancellationToken cancellationToken = default) {
        var metadata = MetadataScope.Current?.Snapshot() ?? EventMetadata.Empty;
        lock (_lock) {
            if (_entries.Any(entry => entry.Source.StreamId == stream))
                return ValueTask.FromResult(false);
            foreach (var request in requests)
                Append(stream, ScheduledFormat.CommandRequestEventType,
                    ScheduledFormat.WriteCommandRequest(request.Owner, request.Due, CommandType(request.Command), "{}"u8), metadata);
        }
        return ValueTask.FromResult(true);
    }

    /// <inheritdoc/>
    public ValueTask<bool> ExistsAsync(string stream, CancellationToken cancellationToken = default) =>
        ValueTask.FromResult(Entries.Any(entry => entry.Source.StreamId == stream));

    /// <inheritdoc/>
    public object? DeserializeCommand(string commandType, ReadOnlyMemory<byte> data) =>
        Deserializer(commandType, data);
}
