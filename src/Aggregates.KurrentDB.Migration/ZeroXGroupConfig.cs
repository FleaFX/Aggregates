using System.Text.Json;
using System.Text.RegularExpressions;

namespace Aggregates.KurrentDB.Migration;

/// <summary>
/// Reads the event-type filters of the 0.x groups out of the server's
/// <c>$persistentSubscriptionConfig</c> stream. The stream's format is internal and not
/// documented; on any deviation the filters come back empty, which blocks the Rollback step
/// but never causes a wrong import.
/// </summary>
static class ZeroXGroupConfig {
    /// <summary>
    /// The system stream the server stores its persistent subscription configuration in.
    /// </summary>
    public const string StreamName = "$persistentSubscriptionConfig";

    /// <summary>
    /// Reads the event-type regular expression of every group on <c>$all</c> out of one
    /// <c>$PersistentConfig</c> event. Entries that are not on $all or do not carry an
    /// event-type regex are skipped; invalid JSON yields an empty result.
    /// </summary>
    /// <param name="json">The data of the config event.</param>
    public static IReadOnlyDictionary<string, string> ParseFilters(ReadOnlyMemory<byte> json) {
        var filters = new Dictionary<string, string>(StringComparer.Ordinal);
        try {
            using var document = JsonDocument.Parse(json);
            if (document.RootElement.ValueKind != JsonValueKind.Object
                || !document.RootElement.TryGetProperty("entries", out var entries) || entries.ValueKind != JsonValueKind.Array)
                return filters;

            foreach (var entry in entries.EnumerateArray()) {
                if (!entry.TryGetProperty("stream", out var stream) || stream.GetString() != "$all")
                    continue;
                if (!entry.TryGetProperty("group", out var group) || group.GetString() is not { } groupName)
                    continue;
                if (!entry.TryGetProperty("filter", out var filter) || !filter.TryGetProperty("context", out var context)
                    || context.GetString() != "eventtype" || !filter.TryGetProperty("type", out var type)
                    || type.GetString() != "regex" || !filter.TryGetProperty("data", out var data)
                    || data.GetString() is not { } regex)
                    continue;
                filters[groupName] = regex;
            }
        } catch (JsonException) {
            // Not readable: the filters stay empty, which blocks Rollback but nothing else.
        }
        return filters;
    }

    /// <summary>
    /// Splits a 0.x event-type regular expression of the form <c>^(?:a|b)$</c> into its names,
    /// with the escapes removed. Returns null for a filter of any other shape.
    /// </summary>
    /// <param name="filter">The regular expression the 0.x group was created with.</param>
    public static IReadOnlyList<string>? ParseFilterNames(string filter) {
        var match = Regex.Match(filter, @"^\^\(\?:(?<names>.*)\)\$$", RegexOptions.CultureInvariant);
        if (!match.Success)
            return null;

        var names = new List<string>();
        foreach (var part in match.Groups["names"].Value.Split('|')) {
            if (part.Length == 0)
                return null;
            names.Add(Regex.Unescape(part));
        }
        return names;
    }
}
