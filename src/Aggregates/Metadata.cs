using System.Collections.Immutable;

namespace Aggregates;

/// <summary>
/// Provides a value that should be added as metadata to a written event.
/// </summary>
/// <typeparam name="TEvent">The type of the event to provide metadata for.</typeparam>
/// <typeparam name="TValue">The type of the metadata value.</typeparam>
public interface IMetadataProvider<in TEvent, out TValue> {
    /// <summary>
    /// Gets the value for a metadata
    /// </summary>
    /// <param name="event">The event to provide metadata for.</param>
    /// <returns>A <typeparamref name="TValue"/>.</returns>
    TValue GetValue(TEvent @event);
}

/// <summary>
/// Enriches an event with metadata when saving.
/// </summary>
[AttributeUsage(AttributeTargets.Class | AttributeTargets.Struct, AllowMultiple = true, Inherited = false)]
public class MetadataAttribute : Attribute {
    readonly string _key;
    readonly Func<object, object> _valueProvider;

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
            select new Func<object, object>(@event => getValueMethod.Invoke(instance, new[] { @event }))
        ).FirstOrDefault();
        if (!(candidate is { } valueProvider)) throw new ArgumentOutOfRangeException(nameof(valueProviderType));

        _key = key;
        _valueProvider = valueProvider;
    }

    /// <summary>
    /// Adds a metadata entry for the given <paramref name="event"/> to the given <paramref name="metadata"/> collection.
    /// </summary>
    /// <param name="metadata"></param>
    /// <param name="event"></param>
    /// <returns></returns>
    public ImmutableDictionary<string, object> Add(ImmutableDictionary<string, object> metadata, object @event) =>
        metadata.Add(_key, _valueProvider(@event));
}