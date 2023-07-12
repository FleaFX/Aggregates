namespace Aggregates.Metadata;

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