using System.Buffers;
using System.Diagnostics.CodeAnalysis;
using System.Globalization;
using System.Text.Json;

namespace Aggregates.Subscriptions;

/// <summary>
/// How parked messages are stored, shared by the parked-message sinks and stores of the storage
/// integrations. Every subscription parks in its own stream (<see cref="ParkedStream"/>), which holds
/// the parked messages and the entries that record a replay or a dismissal. Every event type starts
/// with <c>$</c>, so no subscription delivers these entries.
/// </summary>
public static class ParkedFormat {
    /// <summary>
    /// The event type of a parked message.
    /// </summary>
    public const string ParkedEventType = "$aggregates-parked";

    /// <summary>
    /// The event type of the entry that records a successful replay; its data is the parked message
    /// in link format (<see cref="EventReference.ToLinkData"/>).
    /// </summary>
    public const string ReplayedEventType = "$aggregates-parked-replayed";

    /// <summary>
    /// The event type of the entry that records a dismissal; its data is the parked message in link
    /// format (<see cref="EventReference.ToLinkData"/>).
    /// </summary>
    public const string DismissedEventType = "$aggregates-parked-dismissed";

    /// <summary>
    /// The stream holding the parked messages of <paramref name="subscriptionId"/>.
    /// </summary>
    /// <param name="subscriptionId">The id of the subscription or scheduler that parked them.</param>
    public static string ParkedStream(string subscriptionId) => $"parked-{subscriptionId}";

    /// <summary>
    /// The data of a parked message: a UTF-8 JSON object with the commit position, the stream and
    /// revision of the event (or request) it refers to, the type name of its deserialized event (when
    /// there is one), and the type, message and stack trace of <paramref name="exception"/>.
    /// </summary>
    /// <param name="message">The message that could not be processed.</param>
    /// <param name="exception">Why it could not be processed.</param>
    public static byte[] WriteParked(SubscriptionMessage message, Exception exception) {
        var buffer = new ArrayBufferWriter<byte>();
        using (var writer = new Utf8JsonWriter(buffer)) {
            writer.WriteStartObject();
            writer.WriteNumber("CommitPosition", message.CommitPosition);
            if (!string.IsNullOrEmpty(message.Source.StreamId)) {
                writer.WriteString("Stream", message.Source.StreamId);
                writer.WriteNumber("Revision", message.Source.Revision);
            }
            if (message.Event is { } @event)
                writer.WriteString("EventType", @event.GetType().FullName ?? @event.GetType().Name);
            writer.WriteString("ExceptionType", exception.GetType().FullName ?? exception.GetType().Name);
            writer.WriteString("Message", exception.Message);
            if (exception.StackTrace is { } stackTrace)
                writer.WriteString("StackTrace", stackTrace);
            writer.WriteEndObject();
        }
        return buffer.WrittenSpan.ToArray();
    }

    /// <summary>
    /// Reads a parked message written by <see cref="WriteParked"/>. Never fails: a property that is
    /// missing or invalid gets its default, so that every parked message can at least be listed and
    /// dismissed.
    /// </summary>
    /// <param name="subscriptionId">The id the message was parked under.</param>
    /// <param name="entry">The <see cref="ParkedEventType"/> entry.</param>
    internal static ParkedMessage ReadParked(string subscriptionId, ParkedEntry entry) {
        ulong commitPosition = 0;
        EventReference? source = null;
        string? eventType = null, exceptionType = null, exceptionMessage = null, stackTrace = null;
        try {
            using var document = JsonDocument.Parse(entry.Data);
            var root = document.RootElement;
            if (root.ValueKind == JsonValueKind.Object) {
                if (root.TryGetProperty("CommitPosition", out var position) && position.ValueKind == JsonValueKind.Number)
                    position.TryGetUInt64(out commitPosition);
                if (String(root, "Stream") is { Length: > 0 } stream
                    && root.TryGetProperty("Revision", out var revision) && revision.ValueKind == JsonValueKind.Number
                    && revision.TryGetUInt64(out var revisionValue))
                    source = new EventReference(stream, revisionValue);
                eventType = String(root, "EventType");
                exceptionType = String(root, "ExceptionType");
                exceptionMessage = String(root, "Message");
                stackTrace = String(root, "StackTrace");
            }
        } catch (JsonException) {
        }
        return new ParkedMessage(subscriptionId, Id(entry.Revision), entry.Created, source, commitPosition, eventType,
            exceptionType ?? string.Empty, exceptionMessage ?? string.Empty, stackTrace);
    }

    /// <summary>
    /// Reads which message a <see cref="ReplayedEventType"/> or <see cref="DismissedEventType"/> entry of
    /// the parked stream of <paramref name="subscriptionId"/> closes.
    /// </summary>
    /// <param name="subscriptionId">The id the messages were parked under.</param>
    /// <param name="entry">The replayed or dismissed entry.</param>
    /// <param name="id">The <see cref="ParkedMessage.Id"/> of the message it closes.</param>
    /// <returns><see langword="false"/> when its data is not a link to an entry of that parked stream.</returns>
    internal static bool TryReadClosed(string subscriptionId, ParkedEntry entry, [NotNullWhen(true)] out string? id) {
        if (EventReference.TryParseLinkData(entry.Data.Span, out var reference) && reference.StreamId == ParkedStream(subscriptionId)) {
            id = Id(reference.Revision);
            return true;
        }
        id = null;
        return false;
    }

    /// <summary>
    /// The data of the entry that records the replay or dismissal of <paramref name="message"/>: its
    /// parked entry in link format (<c>{revision}@parked-{subscription id}</c>).
    /// </summary>
    /// <param name="message">A message read by <see cref="ReadParked"/>.</param>
    internal static byte[] WriteClosed(ParkedMessage message) =>
        new EventReference(ParkedStream(message.SubscriptionId), ulong.Parse(message.Id, CultureInfo.InvariantCulture)).ToLinkData();

    // The id of a parked message is the revision of its entry in the parked stream; retention can change that here.
    static string Id(ulong revision) => revision.ToString(CultureInfo.InvariantCulture);

    static string? String(JsonElement element, string name) =>
        element.TryGetProperty(name, out var property) && property.ValueKind == JsonValueKind.String ? property.GetString() : null;
}
