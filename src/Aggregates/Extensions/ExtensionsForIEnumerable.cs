namespace Aggregates.Extensions;

static class ExtensionsForIEnumerable {
    /// <summary>
    /// Performs a side effect for each element in the sequence.
    /// </summary>
    /// <typeparam name="T">The type of the elements contained in the sequence.</typeparam>
    /// <param name="enumerable">The sequence to enumerate.</param>
    /// <param name="act">The <see cref="Action"/> that performs the side effect.</param>
    /// <returns>The unchanged sequence.</returns>
    public static IEnumerable<T> Tap<T>(this IEnumerable<T> enumerable, Action<T> act) {
        foreach (var element in enumerable) {
            act(element);
            yield return element;
        }
    }

    /// <summary>
    /// Performs a side effect for each element in the asynchronous sequence.
    /// </summary>
    /// <typeparam name="T">The type of the elements contained in the sequence.</typeparam>
    /// <param name="asyncEnumerable">The asynchronous sequence to enumerate.</param>
    /// <param name="act">The <see cref="Action"/> that performs the side effect.</param>
    /// <returns>The unchanged sequence.</returns>
    public static async IAsyncEnumerable<T> TapAsync<T>(this IAsyncEnumerable<T> asyncEnumerable, Action<T> act) {
        await foreach (var item in asyncEnumerable) {
            act(item);
            yield return item;
        }
    }
}