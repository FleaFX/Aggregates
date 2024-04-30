using Aggregates.Util;

namespace Aggregates.Metadata;

/// <summary>
/// Provides a value that should be added as metadata to a written event.
/// </summary>
/// <typeparam name="TContext">The type of the context that may provide more information to generate the metadata value.</typeparam>
/// <typeparam name="TValue">The type of the metadata value.</typeparam>
public interface IMetadataProvider<in TContext, TValue> {
    /// <summary>
    /// Gets the value for a metadata
    /// </summary>
    /// <param name="context">The context that may provide more information to generate the metadata value.</param>
    /// <returns>A <typeparamref name="TValue"/>.</returns>
    TValue GetValue(TContext context) =>
        GetValueAsync(context, CancellationToken.None).RunSynchronously();

    /// <summary>
    /// Asynchronously gets the value for a metadata
    /// </summary>
    /// <param name="context">The context that may provide more information to generate the metadata value.</param>
    /// <param name="cancellationToken">A <see cref="CancellationToken" /> to cancel the asynchronous operation.</param>
    /// <returns>A <typeparamref name="TValue"/>.</returns>
    ValueTask<TValue> GetValueAsync(TContext context, CancellationToken cancellationToken) =>
        ValueTask.FromResult(GetValue(context));
}