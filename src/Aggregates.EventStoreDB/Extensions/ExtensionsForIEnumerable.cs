namespace Aggregates.EventStoreDB.Extensions;

static class ExtensionsForIEnumerable {
    /// <summary>
    /// Attempts to map each item in the <paramref name="source"/> to a <typeparamref name="TU"/>, skipping the item when mapping throws an exception.
    /// </summary>
    /// <typeparam name="T">The type of the items in the <paramref name="source"/> sequence.</typeparam>
    /// <typeparam name="TU">The type of the items in the returned sequence.</typeparam>
    /// <param name="source">The sequence to map.</param>
    /// <param name="map">The mapping function.</param>
    /// <returns>A <see cref="IEnumerable{TU}"/>.</returns>
    public static IEnumerable<TU> TrySelect<T, TU>(this IEnumerable<T> source, Func<T, TU> map) {
        foreach (var item in source) {
            TU result;
            try {
                result = map(item);
            } catch (Exception) {
                continue;
            }

            yield return result;
        }
    }
}