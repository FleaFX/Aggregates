using System.Collections;
using System.Collections.Frozen;

namespace Aggregates;

/// <summary>
/// An immutable snapshot of metadata associated with a written or received event.
/// Keys map to either a scalar value (<see cref="MetadataMultiplicity.Single"/>) or an
/// <c>object?[]</c> array (<see cref="MetadataMultiplicity.Multiple"/>).
/// </summary>
/// <remarks>
/// <para>
/// Instances are produced by <see cref="MetadataScope.Snapshot"/> and cannot be constructed
/// directly. Use <see cref="Empty"/> when no metadata is available.
/// </para>
/// <para>
/// <strong>Array invariant:</strong> a value that is an <c>object?[]</c> always represents
/// an accumulated <see cref="MetadataMultiplicity.Multiple"/> collection, never a scalar value
/// that happens to be an array. To store an array as a single unit, serialize it to a non-array
/// type (e.g. a comma-separated string) before adding it to the scope.
/// </para>
/// </remarks>
public sealed class EventMetadata : IReadOnlyDictionary<string, object?> {
    readonly FrozenDictionary<string, object?> _entries;

    /// <summary>
    /// Gets an <see cref="EventMetadata"/> instance with no entries.
    /// </summary>
    public static EventMetadata Empty { get; } = new(FrozenDictionary<string, object?>.Empty);

    internal EventMetadata(Dictionary<string, object?> entries) =>
        _entries = entries.ToFrozenDictionary();

    EventMetadata(FrozenDictionary<string, object?> entries) =>
        _entries = entries;

    /// <inheritdoc/>
    public object? this[string key] => _entries[key];

    /// <inheritdoc/>
    public IEnumerable<string> Keys => _entries.Keys;

    /// <inheritdoc/>
    public IEnumerable<object?> Values => _entries.Values;

    /// <inheritdoc/>
    public int Count => _entries.Count;

    /// <inheritdoc/>
    public bool ContainsKey(string key) => _entries.ContainsKey(key);

    /// <inheritdoc/>
    public bool TryGetValue(string key, out object? value) => _entries.TryGetValue(key, out value);

    /// <inheritdoc/>
    public IEnumerator<KeyValuePair<string, object?>> GetEnumerator() => _entries.GetEnumerator();

    /// <inheritdoc/>
    IEnumerator IEnumerable.GetEnumerator() => ((IEnumerable)_entries).GetEnumerator();
}
