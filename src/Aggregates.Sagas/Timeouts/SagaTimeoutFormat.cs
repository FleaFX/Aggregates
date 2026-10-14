using System.Buffers;
using System.Text.Json;

namespace Aggregates.Sagas;

/// <summary>
/// How saga timeouts are stored, shared by the saga core and the storage integrations. Every event
/// type starts with <see cref="EventTypePrefix"/>, which starts with <c>$</c>, so no subscription
/// delivers these entries; the timeout scheduler reads them with a filter on the prefix.
/// </summary>
public static class SagaTimeoutFormat {
    /// <summary>
    /// The prefix of every timeout event type.
    /// </summary>
    public const string EventTypePrefix = "$aggregates-timeout";

    /// <summary>
    /// The event type of a timeout request in a saga stream.
    /// </summary>
    public const string RequestEventType = "$aggregates-timeout";

    /// <summary>
    /// The event type of the entry the timeout scheduler writes to its own stream once it executed
    /// (or parked) a timeout; its data is the request in link format (<see cref="Aggregates.Subscriptions.EventReference.ToLinkData"/>).
    /// </summary>
    public const string DeliveryEventType = "$aggregates-timeout-delivered";

    /// <summary>
    /// The event type of the markers the timeout scheduler writes to its own stream.
    /// </summary>
    public const string MarkerEventType = "$aggregates-timeout-marker";

    /// <summary>
    /// The data of a timeout request: a UTF-8 JSON object with the owner, the due time
    /// (ISO 8601), the command's stored type name, and the command in base64.
    /// </summary>
    /// <param name="owner">The subscription id of the saga that requested the timeout.</param>
    /// <param name="due">When the timeout is due.</param>
    /// <param name="commandType">The stored type name of the command.</param>
    /// <param name="commandData">The serialized command.</param>
    public static byte[] WriteRequest(string owner, DateTimeOffset due, string commandType, ReadOnlySpan<byte> commandData) {
        var buffer = new ArrayBufferWriter<byte>();
        using (var writer = new Utf8JsonWriter(buffer)) {
            writer.WriteStartObject();
            writer.WriteString("owner", owner);
            writer.WriteString("due", due);
            writer.WriteString("type", commandType);
            writer.WriteBase64String("data", commandData);
            writer.WriteEndObject();
        }
        return buffer.WrittenSpan.ToArray();
    }

    /// <summary>
    /// Reads the data of a timeout request written by <see cref="WriteRequest"/>.
    /// </summary>
    /// <param name="data">The stored data.</param>
    /// <param name="envelope">The request, or <see langword="default"/> when the data is not valid.</param>
    /// <returns><see langword="true"/> when <paramref name="data"/> is a valid timeout request.</returns>
    public static bool TryReadRequest(ReadOnlyMemory<byte> data, out SagaTimeoutEnvelope envelope) {
        envelope = default;
        try {
            using var document = JsonDocument.Parse(data);
            var root = document.RootElement;
            if (root.ValueKind != JsonValueKind.Object
                || !TryGetString(root, "owner", out var owner)
                || !root.TryGetProperty("due", out var due) || due.ValueKind != JsonValueKind.String || !due.TryGetDateTimeOffset(out var dueValue)
                || !TryGetString(root, "type", out var commandType)
                || !root.TryGetProperty("data", out var command) || command.ValueKind != JsonValueKind.String || !command.TryGetBytesFromBase64(out var commandData))
                return false;
            envelope = new SagaTimeoutEnvelope(owner, dueValue, commandType, commandData);
            return true;
        } catch (JsonException) {
            return false;
        }
    }

    static bool TryGetString(JsonElement element, string name, out string value) {
        value = string.Empty;
        if (!element.TryGetProperty(name, out var property) || property.ValueKind != JsonValueKind.String || property.GetString() is not { Length: > 0 } text)
            return false;
        value = text;
        return true;
    }
}
