using System.Collections.Immutable;

namespace Aggregates.Extensions;

public static class ExtensionsForIEnumerable {
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

    /// <summary>
    /// Attempts to copy the given <see cref="Dictionary{TKey,TValue}"/> to a new instance, or returns an empty dictionary if the given <paramref name="source"/> is <see langword="null" />
    /// </summary>
    /// <typeparam name="TKey">The type of the key element.</typeparam>
    /// <typeparam name="TValue">The type of the value element.</typeparam>
    /// <param name="source">The <see cref="Dictionary{TKey,TValue}"/> to copy.</param>
    /// <returns>A <see cref="Dictionary{TKey,TValue}"/>.</returns>
    public static Dictionary<TKey, TValue> CopyOrEmpty<TKey, TValue>(this Dictionary<TKey, TValue>? source)
        where TKey : notnull =>
        source == null ? new Dictionary<TKey, TValue>() : new Dictionary<TKey, TValue>(source);

    /// <summary>
    /// Attempts to peek at the top element in the given <paramref name="stack"/>, or returns the <paramref name="defaultValue"/> if the stack is empty.
    /// </summary>
    /// <typeparam name="T">The type of the elements in the stack.</typeparam>
    /// <param name="stack">The <see cref="ImmutableStack{T}"/> to peek.</param>
    /// <param name="defaultValue">The <typeparamref name="T"/> to return if the stack is empty.</param>
    /// <returns>A <typeparamref name="T"/>.</returns>
    public static T? TryPeek<T>(this ImmutableStack<T> stack, T? defaultValue = default) =>
        stack.IsEmpty ? defaultValue : stack.Peek();
}