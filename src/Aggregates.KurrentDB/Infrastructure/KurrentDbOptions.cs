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
}
