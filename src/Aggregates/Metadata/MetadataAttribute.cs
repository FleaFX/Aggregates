using System.Reflection;

namespace Aggregates.Metadata;

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
            let metaDataType = iface.GetGenericArguments()[0]
            let getValueMethod = iface.GetMethod("GetValue", BindingFlags.Instance | BindingFlags.Public, new[] { metaDataType })
            let instance = Activator.CreateInstance(valueProviderType)
            select new Func<object, object?>(@event => getValueMethod.Invoke(instance, new[] { @event }))
        ).FirstOrDefault();
        if (!(candidate is { } valueProvider)) throw new ArgumentOutOfRangeException(nameof(valueProviderType));

        _key = key;
        _valueProvider = valueProvider;
    }

    /// <summary>
    /// Initializes a new <see cref="MetadataAttribute"/>. Use this constructor when your metadata provider can provide metadata for different context types.
    /// </summary>
    /// <param name="key">A <see cref="string"/> by which the metadata entry will be accessible.</param>
    /// <param name="valueProviderType">The <see cref="Type"/> of the class that implements <see cref="IMetadataProvider{TEvent,TValue}"/> to provide the value of the metadata.</param>
    /// <param name="contextType">The type of the context object.</param>
    public MetadataAttribute(string key, Type valueProviderType, Type contextType) {
        if (string.IsNullOrWhiteSpace(key)) throw new ArgumentNullException(nameof(key));
        if (valueProviderType == null) throw new ArgumentNullException(nameof(valueProviderType));

        // validate that the value provider implements IMetadataProvider
        var candidate = (
            from iface in valueProviderType.GetInterfaces()
            where iface.IsGenericType && iface.GetGenericTypeDefinition() == typeof(IMetadataProvider<,>)
            where iface.GetGenericArguments()[0].IsAssignableTo(contextType)
            let metaDataType = iface.GetGenericArguments()[0]
            let getValueMethod = iface.GetMethod("GetValue", BindingFlags.Instance | BindingFlags.Public, new[] { metaDataType })
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