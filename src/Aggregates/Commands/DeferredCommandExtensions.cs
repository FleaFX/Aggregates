namespace Aggregates;

/// <summary>
/// Defers a command from a saga or a policy.
/// </summary>
public static class DeferredCommandExtensions {
    /// <summary>
    /// Defers <paramref name="command"/> until <paramref name="due"/>: yield the result from a saga's
    /// or a policy's <c>ReactAsync</c> instead of the command itself.
    /// </summary>
    /// <param name="command">The command to execute.</param>
    /// <param name="due">When the command is executed. A time in the past executes it right away.</param>
    /// <exception cref="ArgumentException"><paramref name="command"/> is a <see cref="DeferredCommand"/>.</exception>
    public static DeferredCommand DeferUntil(this ICommand command, DateTimeOffset due) => new(due, command);
}
