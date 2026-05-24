namespace Aggregates;

/// <summary>
/// Enriches written events with metadata derived from the decorated type. Apply to a state or
/// command class to automatically populate the ambient <see cref="MetadataScope"/> whenever an
/// instance of that type is processed by <see cref="EntityRoot{TState,TEvent}"/>.
/// </summary>
/// <remarks>
/// Subclass this attribute and override <see cref="GetValueAsync"/> to supply the metadata value.
/// The context object passed to <see cref="GetValueAsync"/> is the decorated object itself;
/// cast it to the expected type in the implementation.
/// </remarks>
[AttributeUsage(AttributeTargets.Class | AttributeTargets.Struct, AllowMultiple = true, Inherited = true)]
public abstract class MetadataAttribute(string key, MetadataMultiplicity multiplicity = MetadataMultiplicity.Single) : Attribute {
    /// <summary>
    /// Gets the key under which the metadata value will be stored.
    /// </summary>
    public string Key => key;

    /// <summary>
    /// Gets the <see cref="MetadataMultiplicity"/>, indicating whether a new value overwrites
    /// or is accumulated alongside existing values for <see cref="Key"/>.
    /// </summary>
    public MetadataMultiplicity Multiplicity => multiplicity;

    /// <summary>
    /// Asynchronously produces the metadata value for the given <paramref name="context"/> object.
    /// </summary>
    /// <param name="context">
    /// The object the attribute is decorating (a state or command instance).
    /// Cast to the expected type in the implementation.
    /// </param>
    /// <param name="cancellationToken">A cancellation token.</param>
    /// <returns>
    /// A <see cref="ValueTask{TResult}"/> that resolves to the metadata value, or
    /// <see langword="null"/> if no value should be recorded.
    /// </returns>
    public abstract ValueTask<object?> GetValueAsync(object context, CancellationToken cancellationToken);
}
