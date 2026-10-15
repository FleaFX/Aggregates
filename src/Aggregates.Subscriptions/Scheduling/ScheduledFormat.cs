using System.Buffers;
using System.Text;
using System.Text.Json;

namespace Aggregates.Subscriptions;

/// <summary>
/// How scheduled requests are stored, shared by the scheduler and the storage integrations. Every
/// event type starts with <see cref="EventTypePrefix"/>, which starts with <c>$</c>, so no
/// subscription delivers these entries; the scheduler reads them with a filter on the prefix.
/// </summary>
public static class ScheduledFormat {
    /// <summary>
    /// The prefix of every scheduled event type.
    /// </summary>
    public const string EventTypePrefix = "$aggregates-scheduled";

    /// <summary>
    /// The event type of a deferred command request: in a saga stream, or in the request stream of
    /// a policy (<see cref="RequestStream"/>).
    /// </summary>
    public const string CommandRequestEventType = "$aggregates-scheduled-command";

    /// <summary>
    /// The event type of a saga deadline request, in a saga stream: its payload is delivered back to
    /// the saga when it is due.
    /// </summary>
    public const string DeadlineRequestEventType = "$aggregates-scheduled-deadline";

    /// <summary>
    /// The event type of the entry the scheduler writes to its own stream once it executed (or
    /// parked) a request; its data is the request in link format (<see cref="EventReference.ToLinkData"/>).
    /// </summary>
    public const string DeliveryEventType = "$aggregates-scheduled-delivered";

    /// <summary>
    /// The event type of the markers the scheduler writes to its own stream.
    /// </summary>
    public const string MarkerEventType = "$aggregates-scheduled-marker";

    /// <summary>
    /// The id of the scheduler of the saga or policy with <paramref name="subscriptionId"/>: its own
    /// stream, its checkpoint id and its parked-message id.
    /// </summary>
    /// <param name="subscriptionId">The subscription id of the saga or policy.</param>
    public static string SchedulerStream(string subscriptionId) => $"scheduled-{subscriptionId}";

    /// <summary>
    /// The stream holding the deferred commands a policy requested in reaction to
    /// <paramref name="source"/>. It is written once: a second reaction to the same event finds it.
    /// </summary>
    /// <param name="subscriptionId">The subscription id of the policy.</param>
    /// <param name="source">The event the policy reacted to.</param>
    public static string RequestStream(string subscriptionId, EventReference source) =>
        $"scheduled-requests-{subscriptionId}-{source.Revision}@{source.StreamId}";

    /// <summary>
    /// The data of a request, a deferred command or a saga deadline: a UTF-8 JSON object with the
    /// owner, the due time (ISO 8601), the stored type name and the serialized command or payload in
    /// base64, and, for a request made by the reaction to a deadline, that deadline.
    /// </summary>
    /// <param name="owner">The subscription id of the saga or policy that made the request.</param>
    /// <param name="due">When the request is due.</param>
    /// <param name="type">The stored type name of the command or deadline payload.</param>
    /// <param name="data">The serialized command or deadline payload.</param>
    /// <param name="cause">The deadline whose reaction made the request; <see langword="null"/> otherwise.</param>
    public static byte[] WriteRequest(string owner, DateTimeOffset due, string type, ReadOnlySpan<byte> data, EventReference? cause = null) {
        var buffer = new ArrayBufferWriter<byte>();
        using (var writer = new Utf8JsonWriter(buffer)) {
            writer.WriteStartObject();
            writer.WriteString("owner", owner);
            writer.WriteString("due", due);
            writer.WriteString("type", type);
            writer.WriteBase64String("data", data);
            if (cause is { } reference)
                writer.WriteString("cause", reference.ToString());
            writer.WriteEndObject();
        }
        return buffer.WrittenSpan.ToArray();
    }

    /// <summary>
    /// Reads the data of a request written by <see cref="WriteRequest"/>.
    /// </summary>
    /// <param name="data">The stored data.</param>
    /// <param name="envelope">The request, or <see langword="default"/> when the data is not valid.</param>
    /// <returns><see langword="true"/> when <paramref name="data"/> is a valid request.</returns>
    public static bool TryReadRequest(ReadOnlyMemory<byte> data, out ScheduledEnvelope envelope) {
        envelope = default;
        try {
            using var document = JsonDocument.Parse(data);
            var root = document.RootElement;
            if (root.ValueKind != JsonValueKind.Object
                || !TryGetString(root, "owner", out var owner)
                || !root.TryGetProperty("due", out var due) || due.ValueKind != JsonValueKind.String || !due.TryGetDateTimeOffset(out var dueValue)
                || !TryGetString(root, "type", out var type)
                || !root.TryGetProperty("data", out var payload) || payload.ValueKind != JsonValueKind.String || !payload.TryGetBytesFromBase64(out var payloadData)
                || !TryGetCause(root, out var cause))
                return false;
            envelope = new ScheduledEnvelope(owner, dueValue, type, payloadData, cause);
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

    // A request without a cause is valid; a cause that is present must be a reference in link format.
    static bool TryGetCause(JsonElement element, out EventReference? cause) {
        cause = null;
        if (!element.TryGetProperty("cause", out var property))
            return true;
        if (property.ValueKind != JsonValueKind.String
            || !EventReference.TryParseLinkData(Encoding.UTF8.GetBytes(property.GetString()!), out var reference))
            return false;
        cause = reference;
        return true;
    }
}
