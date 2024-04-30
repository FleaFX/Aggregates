namespace Aggregates.Util;

static class AsyncExt {
    /// <summary>
    /// Uses the awaiter of the given <paramref name="valueTask"/> to get the result.
    /// </summary>
    /// <typeparam name="TValue">The type of the returned value.</typeparam>
    /// <param name="valueTask">The <see cref="ValueTask"/> to get the value from.</param>
    /// <returns>A <typeparamref name="TValue"/>.</returns>
    public static TValue RunSynchronously<TValue>(this ValueTask<TValue> valueTask) {
        var awaiter = valueTask.GetAwaiter();
        return awaiter.IsCompleted
            ? awaiter.GetResult()
            : valueTask.AsTask().GetAwaiter().GetResult();
    }
}