namespace Aggregates.Sagas;

/// <summary>
/// Reads the timeout entries of saga streams, appends the entries of the timeout scheduler, and
/// deserializes the planned commands. Implemented by the saga storage integrations
/// (<c>Aggregates.Sagas.KurrentDB</c>, <c>Aggregates.Sagas.MSSP</c>).
/// </summary>
public interface ISagaTimeoutStore {
    /// <summary>
    /// Subscribes to every entry, in any stream, whose event type starts with
    /// <see cref="SagaTimeoutFormat.EventTypePrefix"/>, after <paramref name="after"/> (exclusive;
    /// <see langword="null"/> from the beginning): first the stored entries, then the ones appended
    /// later, in commit order. A store that can report progress yields
    /// <see cref="SagaTimeoutEntry.Progress"/> while it reads other events.
    /// </summary>
    /// <param name="after">The commit position to read after, or <see langword="null"/> from the beginning.</param>
    /// <param name="cancellationToken">A cancellation token.</param>
    IAsyncEnumerable<SagaTimeoutEntry> SubscribeAsync(ulong? after, CancellationToken cancellationToken = default);

    /// <summary>
    /// Appends an entry of <paramref name="eventType"/>, which starts with
    /// <see cref="SagaTimeoutFormat.EventTypePrefix"/>, to <paramref name="stream"/>.
    /// </summary>
    /// <param name="stream">The stream to append to.</param>
    /// <param name="eventType">The event type of the entry.</param>
    /// <param name="data">The data of the entry.</param>
    /// <param name="cancellationToken">A cancellation token.</param>
    ValueTask AppendAsync(string stream, string eventType, ReadOnlyMemory<byte> data, CancellationToken cancellationToken = default);

    /// <summary>
    /// Whether <paramref name="stream"/> holds at least one entry.
    /// </summary>
    /// <param name="stream">The stream to check.</param>
    /// <param name="cancellationToken">A cancellation token.</param>
    ValueTask<bool> ExistsAsync(string stream, CancellationToken cancellationToken = default);

    /// <summary>
    /// Deserializes a planned command with the storage integration's <c>Deserialize</c>;
    /// <see langword="null"/> when the type is unknown. May throw.
    /// </summary>
    /// <param name="commandType">The stored type name of the command.</param>
    /// <param name="data">The serialized command.</param>
    object? DeserializeCommand(string commandType, ReadOnlyMemory<byte> data);
}
