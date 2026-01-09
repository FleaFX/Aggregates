namespace Aggregates.Metadata;

/// <summary>
/// For internal use. Use <see cref="MetadataAttribute{TContext}"/> or <see cref="MetadataAttribute{TContext,TValueProvider}"/> instead.
/// </summary>
public abstract class MetadataAttribute(MetadataMultiplicity multiplicity = MetadataMultiplicity.Single) : Attribute {
    /// <summary>
    /// Gets the <see cref="MetadataMultiplicity"/>, indicating whether just a single one or multiple values are allowed.
    /// </summary>
    public MetadataMultiplicity Multiplicity => multiplicity;

    /// <summary>
    /// Creates a <see cref="KeyValuePair"/> to be used in a metadata dictionary using the given <paramref name="context"/>.
    /// </summary>
    /// <param name="context">A context object that may provide more information to create the metadata.</param>
    /// <returns></returns>
    public abstract KeyValuePair<string, object?> Create(object context);
}

/// <summary>
/// Enriches an event with metadata when saving.
/// </summary>
[AttributeUsage(AttributeTargets.Class | AttributeTargets.Struct, AllowMultiple = true)]
public class MetadataAttribute<TContext>(string key, MetadataMultiplicity multiplicity = MetadataMultiplicity.Single) : MetadataAttribute(multiplicity) where TContext : IMetadataProvider<TContext> {
    /// <summary>
    /// Creates a <see cref="KeyValuePair"/> to be used in a metadata dictionary using the given <paramref name="context"/>.
    /// </summary>
    /// <param name="context">A context object that may provide more information to create the metadata.</param>
    /// <returns></returns>
    public override KeyValuePair<string, object?> Create(object context) {
        var self = (TContext)context;
        return new KeyValuePair<string, object?>(key, self.GetValue(key, self));
    }
}

/// <summary>
/// Enriches an event with metadata when saving.
/// </summary>
[AttributeUsage(AttributeTargets.Class | AttributeTargets.Struct, AllowMultiple = true)]
public class MetadataAttribute<TContext, TValueProvider>(string key, MetadataMultiplicity multiplicity = MetadataMultiplicity.Single) : MetadataAttribute(multiplicity) where TValueProvider : IMetadataProvider<TContext> {
    readonly Func<string, TContext, object?> _valueProvider = Activator.CreateInstance<TValueProvider>().GetValue;

    /// <summary>
    /// Creates a <see cref="KeyValuePair"/> to be used in a metadata dictionary using the given <paramref name="context"/>.
    /// </summary>
    /// <param name="context">A context object that may provide more information to create the metadata.</param>
    /// <returns></returns>
    public override KeyValuePair<string, object?> Create(object context) =>
        new(key, _valueProvider(key, (TContext)context));
}