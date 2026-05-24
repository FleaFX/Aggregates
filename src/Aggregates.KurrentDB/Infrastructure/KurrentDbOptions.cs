namespace Aggregates.KurrentDB;

/// <summary>
/// Represents a domain event serialized for storage in KurrentDB.
/// </summary>
/// <param name="EventType">
/// The event type name stored alongside the data in KurrentDB (used for deserialization).
/// </param>
/// <param name="Data">The serialized event payload.</param>
public readonly record struct SerializedEvent(string EventType, ReadOnlyMemory<byte> Data);

/// <summary>
/// Configuration options for <see cref="ServiceCollectionExtensions.AddKurrentDb"/>.
/// </summary>
public sealed class KurrentDbOptions {
    /// <summary>
    /// Serializes a domain event to a <see cref="SerializedEvent"/> for storage in KurrentDB.
    /// </summary>
    public Func<object, SerializedEvent>? Serialize { get; set; }

    /// <summary>
    /// Deserializes a KurrentDB-stored event back to a domain event.
    /// Returns <see langword="null"/> when the event type is unknown; the event is then skipped.
    /// </summary>
    public Func<string, ReadOnlyMemory<byte>, object?>? Deserialize { get; set; }

    /// <summary>
    /// Serializes an <see cref="EventMetadata"/> snapshot to bytes for storage alongside the
    /// event in KurrentDB. Optional: when <see langword="null"/>, no metadata is written.
    /// </summary>
    public Func<EventMetadata, ReadOnlyMemory<byte>>? SerializeMetadata { get; set; }

    /// <summary>
    /// Deserializes the raw metadata bytes stored in KurrentDB back to an <see cref="EventMetadata"/>.
    /// Optional: when <see langword="null"/>, <see cref="EventMetadata.Empty"/> is used for all
    /// events delivered through subscriptions.
    /// </summary>
    public Func<ReadOnlyMemory<byte>, EventMetadata>? DeserializeMetadata { get; set; }
}
