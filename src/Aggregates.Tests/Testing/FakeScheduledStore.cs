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
    /// The exception <see cref="SubscribeAsync"/> throws instead of an <see cref="OperationCanceledException"/>
    /// once its token is cancelled, like a transport that reports a cancellation as its own exception.
    /// </summary>
    public Exception? CancellationException { get; set; }

    /// <summary>
    /// Deserializes a command or deadline payload; returns null for an unknown type by default.
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
        Append(sagaId, ScheduledFormat.CommandRequestEventType, ScheduledFormat.WriteRequest(owner, due, commandType, "{}"u8), metadata);

    /// <summary>
    /// Appends a saga deadline request of <paramref name="owner"/> to the stream <paramref name="sagaId"/>.
    /// </summary>
    public ScheduledEntry AppendDeadline(string sagaId, string owner, DateTimeOffset due, string payloadType = "Test.Deadline@v1", EventReference? cause = null, EventMetadata? metadata = null) =>
        Append(sagaId, ScheduledFormat.DeadlineRequestEventType, ScheduledFormat.WriteRequest(owner, due, payloadType, "{}"u8, cause), metadata);

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
        while (true) {
            bool readable;
            try {
                readable = await channel.Reader.WaitToReadAsync(cancellationToken);
            } catch (OperationCanceledException) when (CancellationException is { } cancellation && cancellationToken.IsCancellationRequested) {
                throw cancellation;
            }
            if (!readable)
                yield break;
            yield return await channel.Reader.ReadAsync();
        }
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
                    ScheduledFormat.WriteRequest(request.Owner, request.Due, CommandType(request.Command), "{}"u8, request.Cause), metadata);
        }
        return ValueTask.FromResult(true);
    }

    /// <inheritdoc/>
    public ValueTask<bool> ExistsAsync(string stream, CancellationToken cancellationToken = default) =>
        ValueTask.FromResult(Entries.Any(entry => entry.Source.StreamId == stream));

    /// <inheritdoc/>
    public object? Deserialize(string type, ReadOnlyMemory<byte> data) =>
        Deserializer(type, data);
}
