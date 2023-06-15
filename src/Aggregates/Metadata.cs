using Aggregates.Util;
using System.Collections.Immutable;
using Aggregates.Extensions;

namespace Aggregates;

/// <summary>
/// Provides a value that should be added as metadata to a written event.
/// </summary>
/// <typeparam name="TContext">The type of the context that may provide more information to generate the metadata value.</typeparam>
/// <typeparam name="TValue">The type of the metadata value.</typeparam>
public interface IMetadataProvider<in TContext, out TValue> {
    /// <summary>
    /// Gets the value for a metadata
    /// </summary>
    /// <param name="context">The context that may provide more information to generate the metadata value.</param>
    /// <returns>A <typeparamref name="TValue"/>.</returns>
    TValue GetValue(TContext context);
}

/// <summary>
/// Enriches an event with metadata when saving.
/// </summary>
[AttributeUsage(AttributeTargets.Class | AttributeTargets.Struct, AllowMultiple = true, Inherited = true)]
public class MetadataAttribute : Attribute {
    readonly string _key;
    readonly Func<object, object?> _valueProvider;

    /// <summary>
    /// Initializes a new <see cref="MetadataAttribute"/>.
    /// </summary>
    /// <param name="key">A <see cref="string"/> by which the metadata entry will be accessible.</param>
    /// <param name="valueProviderType">The <see cref="Type"/> of the class that implements <see cref="IMetadataProvider{TEvent,TValue}"/> to provide the value of the metadata.</param>
    public MetadataAttribute(string key, Type valueProviderType) {
        if (string.IsNullOrWhiteSpace(key)) throw new ArgumentNullException(nameof(key));
        if (valueProviderType == null) throw new ArgumentNullException(nameof(valueProviderType));

        // validate that the value provider implements IMetadataProvider
        var candidate = (
            from iface in valueProviderType.GetInterfaces()
            where iface.IsGenericType && iface.GetGenericTypeDefinition() == typeof(IMetadataProvider<,>)
            let getValueMethod = iface.GetMethod("GetValue")
            let instance = Activator.CreateInstance(valueProviderType)
            select new Func<object, object?>(@event => getValueMethod.Invoke(instance, new[] { @event }))
        ).FirstOrDefault();
        if (!(candidate is { } valueProvider)) throw new ArgumentOutOfRangeException(nameof(valueProviderType));

        _key = key;
        _valueProvider = valueProvider;
    }

    /// <summary>
    /// Creates a <see cref="KeyValuePair"/> to be used in a metadata dictionary using the given <paramref name="context"/>.
    /// </summary>
    /// <param name="context">A context object that may provide more information to create the metadata.</param>
    /// <returns></returns>
    internal KeyValuePair<string, object?> Create(object context) =>
        new(_key, _valueProvider(context));
}

/// <summary>
/// Provides a scope object into which event metadata can be collected while handling a command.
/// </summary>
sealed class MetadataScope : IAsyncDisposable {
    readonly Dictionary<string, object?> _metadata;

    /// <summary>
    /// Initializes a new <see cref="MetadataScope"/>.
    /// </summary>
    public MetadataScope() {
        // attempt to copy values from outer scopes
        _metadata = (Scopes.TryPeek()?._metadata).CopyOrEmpty();

        Scopes = Scopes.Push(this);
    }

    /// <summary>
    /// Gets the current <see cref="MetadataScope"/>.
    /// </summary>
    /// <exception cref="InvalidOperationException">Thrown if there is no current scope.</exception>
    public static MetadataScope Current => Scopes.TryPeek(defaultValue: new MetadataScope())!;

    /// <summary>
    /// Adds the given metadata to the scope.
    /// </summary>
    /// <param name="metadata">The metadata to add.</param>
    public void Add(KeyValuePair<string, object?> metadata) =>
        _metadata.Add(metadata.Key, metadata.Value);

    /// <summary>
    /// Returns the metadata in the current scope as a <see cref="IDictionary{TKey,TValue}"/>.
    /// </summary>
    /// <returns>A <see cref="IDictionary{TKey,TValue}"/>.</returns>
    public IDictionary<string, object?> ToDictionary() =>
        _metadata.ToImmutableDictionary();

    static readonly string ThreadId = Guid.NewGuid().ToString("N");
    static ImmutableStack<MetadataScope> Scopes {
        get => CallContext<ImmutableStack<MetadataScope>>.LogicalGetData(ThreadId) ?? ImmutableStack.Create<MetadataScope>();
        set => CallContext<ImmutableStack<MetadataScope>>.LogicalSetData(ThreadId, value);
    }

    /// <summary>Performs application-defined tasks associated with freeing, releasing, or resetting unmanaged resources asynchronously.</summary>
    /// <returns>A task that represents the asynchronous dispose operation.</returns>
    public ValueTask DisposeAsync() {
        if (!Scopes.IsEmpty)
            Scopes = Scopes.Pop();

        return ValueTask.CompletedTask;
    }
}