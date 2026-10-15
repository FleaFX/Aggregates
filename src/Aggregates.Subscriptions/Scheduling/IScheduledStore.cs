namespace Aggregates.Subscriptions;

/// <summary>
/// Reads the scheduled entries of every stream, appends the entries of the scheduler and the
/// requests of policies, and deserializes deferred commands. Implemented by the storage
/// integrations (<c>Aggregates.KurrentDB</c>, <c>Aggregates.MSSP</c>).
/// </summary>
public interface IScheduledStore {
    /// <summary>
    /// Subscribes to every entry, in any stream, whose event type starts with
    /// <see cref="ScheduledFormat.EventTypePrefix"/>, after <paramref name="after"/> (exclusive;
    /// <see langword="null"/> from the beginning): first the stored entries, then the ones appended
    /// later, in commit order. A store that can report progress yields
    /// <see cref="ScheduledEntry.Progress"/> while it reads other events.
    /// </summary>
    /// <param name="after">The commit position to read after, or <see langword="null"/> from the beginning.</param>
    /// <param name="cancellationToken">A cancellation token.</param>
    IAsyncEnumerable<ScheduledEntry> SubscribeAsync(ulong? after, CancellationToken cancellationToken = default);

    /// <summary>
    /// Appends an entry of <paramref name="eventType"/>, which starts with
    /// <see cref="ScheduledFormat.EventTypePrefix"/>, to <paramref name="stream"/>.
    /// </summary>
    /// <param name="stream">The stream to append to.</param>
    /// <param name="eventType">The event type of the entry.</param>
    /// <param name="data">The data of the entry.</param>
    /// <param name="cancellationToken">A cancellation token.</param>
    ValueTask AppendAsync(string stream, string eventType, ReadOnlyMemory<byte> data, CancellationToken cancellationToken = default);

    /// <summary>
    /// Stores <paramref name="requests"/> as <see cref="ScheduledFormat.CommandRequestEventType"/>
    /// entries in one append to <paramref name="stream"/>, which must not exist yet, with the
    /// metadata of the current <see cref="MetadataScope"/>.
    /// </summary>
    /// <param name="stream">The stream to create.</param>
    /// <param name="requests">The requests to store; at least one.</param>
    /// <param name="cancellationToken">A cancellation token.</param>
    /// <returns><see langword="false"/> when <paramref name="stream"/> exists already: nothing is written.</returns>
    ValueTask<bool> TryAppendRequestsAsync(string stream, IReadOnlyList<DeferredCommandRequest> requests, CancellationToken cancellationToken = default);

    /// <summary>
    /// Whether <paramref name="stream"/> holds at least one entry.
    /// </summary>
    /// <param name="stream">The stream to check.</param>
    /// <param name="cancellationToken">A cancellation token.</param>
    ValueTask<bool> ExistsAsync(string stream, CancellationToken cancellationToken = default);

    /// <summary>
    /// Deserializes a deferred command with the storage integration's <c>Deserialize</c>;
    /// <see langword="null"/> when the type is unknown. May throw.
    /// </summary>
    /// <param name="commandType">The stored type name of the command.</param>
    /// <param name="data">The serialized command.</param>
    object? DeserializeCommand(string commandType, ReadOnlyMemory<byte> data);
}
