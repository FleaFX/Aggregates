namespace Aggregates.Extensions;

static class ExtensionsForAction {
    public static Action<T> AndThen<T>(this Action<T>? action, Action<T> next) {
        var first = action;
        return arg => {
            first?.Invoke(arg);
            next(arg);
        };
    }
}