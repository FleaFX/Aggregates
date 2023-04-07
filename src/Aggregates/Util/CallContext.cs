using System.Collections.Concurrent;

namespace Aggregates.Util;

static class CallContext<T> {
    static readonly ConcurrentDictionary<string, AsyncLocal<T>> State = new();

    /// <summary>
    /// Stores a given object and associates it with the specified name.
    /// </summary>
    /// <typeparam name="T">The type of the stored object.</typeparam>
    /// <param name="name">The name with which to associate the new item in the call context.</param>
    /// <param name="data">The object to store in the call context.</param>
    /// <returns>The given instance of <typeparamref name="T"/>.</returns>
    public static T LogicalSetData(string name, T data) {
        State.GetOrAdd(name, _ => new AsyncLocal<T>()).Value = data;
        return LogicalGetData(name)!;
    }

    /// <summary>
    /// Retrieves an object with the specified name from the <see cref="CallContext{T}"/>.
    /// </summary>
    /// <param name="name">The name of the item in the call context.</param>
    /// <returns>The object in the call context associated with the specified name, or a default value for <typeparamref name="T"/> if none is found.</returns>
    public static T? LogicalGetData(string name) =>
        State.TryGetValue(name, out var data) ? data.Value : default;
}