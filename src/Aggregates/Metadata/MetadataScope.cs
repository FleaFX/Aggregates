using Aggregates.Extensions;
using Aggregates.Util;
using System.Collections.Immutable;

namespace Aggregates.Metadata;

/// <summary>
/// Provides a scope object into which event metadata can be collected while handling a command.
/// </summary>
public sealed class MetadataScope : IAsyncDisposable, IDisposable {
    readonly Dictionary<string, object?> _metadata;

    /// <summary>
    /// Initializes a new <see cref="MetadataScope"/>.
    /// </summary>
    internal MetadataScope(bool noPush) {
        // attempt to copy values from outer scopes
        _metadata = (Scopes.TryPeek()?._metadata).CopyOrEmpty();

        if (!noPush)
            Scopes = Scopes.Push(this);
    }

    /// <summary>
    /// Initializes a new <see cref="MetadataScope"/>.
    /// </summary>
    public MetadataScope() : this(false) { }

    /// <summary>
    /// Gets the current <see cref="MetadataScope"/>.
    /// </summary>
    /// <exception cref="InvalidOperationException">Thrown if there is no current scope.</exception>
    public static MetadataScope Current => Scopes.TryPeek(defaultValue: new MetadataScope(true))!;

    /// <summary>
    /// Adds the given metadata to the scope.
    /// </summary>
    /// <param name="metadata">The metadata to add.</param>
    /// <param name="multiplicity">Indicates whether a value overwrites or compliments existing values for the given key.</param>
    public void Add(KeyValuePair<string, object?> metadata, MetadataMultiplicity multiplicity = MetadataMultiplicity.Single) =>
        _metadata[metadata.Key] =
            _metadata.TryGetValue(metadata.Key, out var existingValue) && existingValue is not null
                ? multiplicity switch {
                    MetadataMultiplicity.Single => metadata.Value,
                    MetadataMultiplicity.Multiple => !existingValue.GetType().IsArray
                        ? [existingValue, metadata.Value]
                        : (object?[]) [..(Array)existingValue, metadata.Value],
                    _ => throw new ArgumentOutOfRangeException(nameof(multiplicity), multiplicity, null)
                }
                : metadata.Value;

    /// <summary>
    /// Adds the given metadata to the scope.
    /// </summary>
    /// <param name="key">The key by which the metadata will be referable.</param>
    /// <param name="value">The value of the metadata.</param>
    /// <param name="multiplicity">Indicates whether a value overwrites or compliments existing values for the given key.</param>
    public void Add(string key, object? value, MetadataMultiplicity multiplicity = MetadataMultiplicity.Single) =>
        Add(new KeyValuePair<string, object?>(key, value), multiplicity);

    /// <summary>
    /// Returns the metadata in the current scope as a <see cref="IDictionary{TKey,TValue}"/>.
    /// </summary>
    /// <returns>A <see cref="IDictionary{TKey,TValue}"/>.</returns>
    public IDictionary<string, object?> ToDictionary() =>
        _metadata.AsReadOnly();

    static readonly string ThreadId = Guid.NewGuid().ToString("N");
    static ImmutableStack<MetadataScope> Scopes {
        get => CallContext<ImmutableStack<MetadataScope>>.LogicalGetData(ThreadId) ?? ImmutableStack.Create<MetadataScope>();
        set => CallContext<ImmutableStack<MetadataScope>>.LogicalSetData(ThreadId, value);
    }

    /// <summary>Performs application-defined tasks associated with freeing, releasing, or resetting unmanaged resources asynchronously.</summary>
    /// <returns>A task that represents the asynchronous dispose operation.</returns>
    ValueTask IAsyncDisposable.DisposeAsync() {
        Dispose();

        return ValueTask.CompletedTask;
    }

    /// <summary>
    /// 
    /// </summary>
    /// <exception cref="NotImplementedException"></exception>
    public void Dispose() {
        if (!Scopes.IsEmpty)
            Scopes = Scopes.Pop();
    }
}