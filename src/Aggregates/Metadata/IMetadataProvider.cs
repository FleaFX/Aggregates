using Aggregates.Util;

namespace Aggregates.Metadata;

/// <summary>
/// Provides a value that should be added as metadata to a written event.
/// </summary>
/// <typeparam name="TContext">The type of the context that may provide more information to generate the metadata value.</typeparam>
public interface IMetadataProvider<in TContext> {
    /// <summary>
    /// Gets the value for a metadata
    /// </summary>
    /// <param name="key">The key for which a value is requested.</param>
    /// <param name="context">The context that may provide more information to generate the metadata value.</param>
    /// <returns>A <see cref="object"/>.</returns>
    object GetValue(string key, TContext context) =>
        GetValueAsync(key, context, CancellationToken.None).RunSynchronously();

    /// <summary>
    /// Asynchronously gets the value for a metadata
    /// </summary>
    /// <param name="key">The key for which a value is requested.</param>
    /// <param name="context">The context that may provide more information to generate the metadata value.</param>
    /// <param name="cancellationToken">A <see cref="CancellationToken" /> to cancel the asynchronous operation.</param>
    /// <returns>A <see cref="ValueTask{TResult}"/> that represents the asynchronous operation.</returns>
    ValueTask<object> GetValueAsync(string key, TContext context, CancellationToken cancellationToken) =>
        ValueTask.FromResult(GetValue(key, context));
}