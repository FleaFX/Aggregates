using System.Collections.Immutable;

namespace Aggregates;

/// <summary>
/// Collects metadata entries for the event(s) written during a single command execution.
/// Opened by <see cref="MetadataAwareCommandHandler{TCommand}"/> and consumed by the storage
/// integration's commit handler via <see cref="Snapshot"/>.
/// </summary>
/// <remarks>
/// <para>
/// The scope is ambient: access it via <see cref="Current"/>. When no scope is active,
/// <see cref="Current"/> returns <see langword="null"/>; use the null-conditional operator
/// (<c>MetadataScope.Current?.Add(...)</c>) to add entries safely.
/// </para>
/// <para>
/// Scopes nest: creating a new <see cref="MetadataScope"/> pushes it onto the ambient stack.
/// The new scope starts with an optional seed taken from the parent (e.g. incoming event
/// metadata propagated through a saga or policy). Disposing pops the scope.
/// </para>
/// <para>
/// Internally, <see cref="MetadataMultiplicity.Multiple"/> entries are tracked as
/// <see cref="List{T}"/> accumulators, never as raw arrays. This prevents the array-in-array
/// ambiguity that arises when a scope is seeded from a previously snapshotted
/// <see cref="EventMetadata"/> (where all <c>Multiple</c> values appear as arrays).
/// </para>
/// </remarks>
public sealed class MetadataScope : IAsyncDisposable {
    static readonly AsyncLocal<ImmutableStack<MetadataScope>> _scopes = new();

    static ImmutableStack<MetadataScope> Scopes {
        get => _scopes.Value ?? ImmutableStack<MetadataScope>.Empty;
        set => _scopes.Value = value;
    }

    // Value is List<object?> when IsMultiple == true, otherwise the scalar value.
    readonly Dictionary<string, (object? Value, bool IsMultiple)> _entries = new();

    /// <summary>
    /// Initializes a new <see cref="MetadataScope"/> with no initial entries and pushes it onto
    /// the ambient stack.
    /// </summary>
    public MetadataScope() => Scopes = Scopes.Push(this);

    /// <summary>
    /// Initializes a new <see cref="MetadataScope"/> seeded from <paramref name="seed"/> and
    /// pushes it onto the ambient stack. Entries present in the seed are available immediately;
    /// array values in the seed are restored as accumulators so further
    /// <see cref="MetadataMultiplicity.Multiple"/> additions append correctly.
    /// </summary>
    /// <param name="seed">The <see cref="EventMetadata"/> to seed from.</param>
    public MetadataScope(EventMetadata seed) {
        foreach (var (key, value) in seed)
            // Array values in the snapshot were produced by Multiple accumulation; restore them
            // as List<object?> accumulators so further Multiple additions keep appending.
            _entries[key] = value is object?[] arr
                ? (new List<object?>(arr), true)
                : (value, false);
        Scopes = Scopes.Push(this);
    }

    /// <summary>
    /// Gets the currently active <see cref="MetadataScope"/>, or <see langword="null"/> when
    /// no scope is active.
    /// </summary>
    public static MetadataScope? Current =>
        Scopes.IsEmpty ? null : Scopes.Peek();

    /// <summary>
    /// Adds or updates an entry in this scope.
    /// </summary>
    /// <param name="key">The metadata key.</param>
    /// <param name="value">The value to record.</param>
    /// <param name="multiplicity">
    /// <see cref="MetadataMultiplicity.Single"/> overwrites any existing value.
    /// <see cref="MetadataMultiplicity.Multiple"/> appends to the accumulated collection;
    /// duplicate values are silently ignored.
    /// </param>
    public void Add(string key, object? value, MetadataMultiplicity multiplicity = MetadataMultiplicity.Single) {
        if (!_entries.TryGetValue(key, out var existing)) {
            _entries[key] = multiplicity == MetadataMultiplicity.Multiple
                ? (new List<object?> { value }, true)
                : (value, false);
            return;
        }

        if (multiplicity == MetadataMultiplicity.Single) {
            _entries[key] = (value, false);
        } else if (existing.IsMultiple && existing.Value is List<object?> list) {
            if (!list.Contains(value)) list.Add(value);
        } else {
            // Was Single, first Multiple addition: start a new accumulator with both values.
            _entries[key] = (new List<object?> { existing.Value, value }, true);
        }
    }

    /// <summary>
    /// Materializes the current entries into an immutable <see cref="EventMetadata"/> snapshot.
    /// <see cref="MetadataMultiplicity.Multiple"/> accumulators are converted to arrays.
    /// </summary>
    /// <returns>An <see cref="EventMetadata"/> representing the current state of this scope.</returns>
    public EventMetadata Snapshot() {
        var dict = new Dictionary<string, object?>(_entries.Count);
        foreach (var (key, (value, isMultiple)) in _entries)
            dict[key] = isMultiple
                ? ((List<object?>)value!).ToArray()
                : value;
        return new EventMetadata(dict);
    }

    /// <inheritdoc/>
    public ValueTask DisposeAsync() {
        if (!Scopes.IsEmpty)
            Scopes = Scopes.Pop();
        return ValueTask.CompletedTask;
    }
}
