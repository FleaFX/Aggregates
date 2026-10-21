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
/// <see cref="MetadataScope.Snapshot"/> produces the metadata of written events. For received
/// events, the storage integration's <c>DeserializeMetadata</c> delegate creates it with the
/// public constructor. Use <see cref="Empty"/> when no metadata is available.
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

    /// <summary>
    /// Initializes a new <see cref="EventMetadata"/> with the given entries.
    /// </summary>
    /// <param name="entries">
    /// The key/value pairs. Keys are compared ordinally; when a key occurs more than once, the
    /// last value wins. The pairs are copied, the values are not: an array or dictionary value
    /// is shared with the caller. Represent multiple values as <c>object?[]</c> (see remarks).
    /// </param>
    /// <exception cref="ArgumentNullException">
    /// <paramref name="entries"/> is <see langword="null"/>, or contains a <see langword="null"/> key.
    /// </exception>
    public EventMetadata(IEnumerable<KeyValuePair<string, object?>> entries) =>
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
