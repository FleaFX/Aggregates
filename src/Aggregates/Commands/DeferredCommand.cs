namespace Aggregates;

/// <summary>
/// A command a saga or a policy defers until later. Yield it from <c>ReactAsync</c> among the
/// commands, usually with <see cref="DeferredCommandExtensions.DeferUntil"/>: it is stored, and once
/// <see cref="Due"/> has passed, <see cref="Command"/> is executed like any other command. Execution
/// is at least once and can be late, so the command decides from the state of its aggregate whether
/// it still applies — for example, a reminder for a bill that has been paid in the meantime does nothing.
/// </summary>
/// <remarks>
/// The command waits in the store until it is due, so the storage integration's <c>Serialize</c> and
/// <c>Deserialize</c> must know its type under a stable name. Only sagas and policies can defer a
/// command; <see cref="ICommandDispatcher"/> has no handler for it.
/// </remarks>
public sealed record DeferredCommand : ICommand {
    /// <summary>
    /// Initializes a new <see cref="DeferredCommand"/>.
    /// </summary>
    /// <param name="due">When the command is executed. A time in the past executes it right away.</param>
    /// <param name="command">The command to execute.</param>
    /// <exception cref="ArgumentException"><paramref name="command"/> is a <see cref="DeferredCommand"/>.</exception>
    public DeferredCommand(DateTimeOffset due, ICommand command) {
        ArgumentNullException.ThrowIfNull(command);
        if (command is DeferredCommand)
            throw new ArgumentException("A deferred command can't hold another deferred command.", nameof(command));
        Due = due;
        Command = command;
    }

    /// <summary>
    /// When the command is executed.
    /// </summary>
    public DateTimeOffset Due { get; }

    /// <summary>
    /// The command executed at <see cref="Due"/>.
    /// </summary>
    public ICommand Command { get; }
}
