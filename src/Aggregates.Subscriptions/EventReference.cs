using System.Buffers.Text;
using System.Globalization;
using System.Text;

namespace Aggregates.Subscriptions;

/// <summary>
/// Identifies a stored event by its stream and its revision within that stream.
/// </summary>
/// <param name="StreamId">The stream the event belongs to.</param>
/// <param name="Revision">The revision of the event within its stream.</param>
public readonly record struct EventReference(string StreamId, ulong Revision) {
    /// <summary>
    /// The event type of a link event: "$>".
    /// </summary>
    public const string LinkEventType = "$>";

    /// <summary>
    /// The payload of a link event to this event: "{Revision}@{StreamId}", UTF-8.
    /// </summary>
    public byte[] ToLinkData() =>
        Encoding.UTF8.GetBytes(string.Create(CultureInfo.InvariantCulture, $"{Revision}@{StreamId}"));

    /// <summary>
    /// Parses the payload of a link event. The revision ends at the first '@'; the rest is the
    /// stream id (which may itself contain '@', e.g. "Orders@v1-1").
    /// </summary>
    /// <param name="data">The payload of the link event to parse.</param>
    /// <param name="reference">
    /// The parsed reference, or <see langword="default"/> when <paramref name="data"/> is not a
    /// valid link payload.
    /// </param>
    /// <returns>
    /// <see langword="true"/> when <paramref name="data"/> is a valid link payload; otherwise
    /// <see langword="false"/>.
    /// </returns>
    public static bool TryParseLinkData(ReadOnlySpan<byte> data, out EventReference reference) {
        var separator = data.IndexOf((byte)'@');
        if (separator <= 0) {
            reference = default;
            return false;
        }

        var revision = data[..separator];
        if (!Utf8Parser.TryParse(revision, out ulong value, out var consumed) || consumed != revision.Length) {
            reference = default;
            return false;
        }

        var streamId = Encoding.UTF8.GetString(data[(separator + 1)..]);
        if (string.IsNullOrEmpty(streamId)) {
            reference = default;
            return false;
        }

        reference = new EventReference(streamId, value);
        return true;
    }

    /// <summary>
    /// The reference in link payload format: "{Revision}@{StreamId}".
    /// </summary>
    public override string ToString() => string.Create(CultureInfo.InvariantCulture, $"{Revision}@{StreamId}");
}
