using MSSP;

namespace Aggregates.MSSP;

/// <summary>
/// Options for configuring the MSSP integration in Aggregates.
/// </summary>
public sealed class MsspOptions {
    /// <summary>
    /// Serializes a domain event to a <see cref="EventData"/> for storage in MSSP.
    /// </summary>
    public Func<object, EventData>? Serialize { get; set; }

    /// <summary>
    /// Deserializes a MSSP-stored event back to a domain event.
    /// Returns <see langword="null"/> when the event type is unknown; the event is then skipped.
    /// </summary>
    public Func<string, ReadOnlyMemory<byte>, object?>? Deserialize { get; set; }

    /// <summary>
    /// Serializes an <see cref="EventMetadata"/> snapshot to bytes for storage alongside the
    /// event in MSSP. Optional: when <see langword="null"/>, no metadata is written.
    /// </summary>
    public Func<EventMetadata, ReadOnlyMemory<byte>>? SerializeMetadata { get; set; }

    /// <summary>
    /// Deserializes the raw metadata bytes stored in MSSP back to an <see cref="EventMetadata"/>.
    /// Optional: when <see langword="null"/>, <see cref="EventMetadata.Empty"/> is used for all
    /// events delivered through subscriptions.
    /// </summary>
    public Func<ReadOnlyMemory<byte>, EventMetadata>? DeserializeMetadata { get; set; }
}
