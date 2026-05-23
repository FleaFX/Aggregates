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
}
