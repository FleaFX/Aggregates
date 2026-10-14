using Aggregates.Subscriptions;

namespace Aggregates.Sagas;

/// <summary>
/// An entry whose event type starts with <see cref="SagaTimeoutFormat.EventTypePrefix"/>, as an
/// <see cref="ISagaTimeoutStore"/> reads it from the store, or a progress report.
/// </summary>
/// <param name="EventType">The stored event type.</param>
/// <param name="Data">The stored data.</param>
/// <param name="Metadata">The deserialized metadata; <see cref="EventMetadata.Empty"/> when there is none or it could not be deserialized.</param>
/// <param name="Source">The stream and revision of the entry.</param>
/// <param name="CommitPosition">The commit position of the entry.</param>
public readonly record struct SagaTimeoutEntry(string EventType, ReadOnlyMemory<byte> Data, EventMetadata Metadata, EventReference Source, ulong CommitPosition) {
    /// <summary>
    /// True for a report without an entry: the store read every event up to
    /// <see cref="CommitPosition"/> and found no other timeout entry.
    /// </summary>
    public bool IsProgress { get; init; }

    /// <summary>
    /// A progress report at <paramref name="position"/>.
    /// </summary>
    /// <param name="position">The commit position the store has read up to.</param>
    public static SagaTimeoutEntry Progress(ulong position) =>
        new(string.Empty, default, EventMetadata.Empty, default, position) { IsProgress = true };
}
