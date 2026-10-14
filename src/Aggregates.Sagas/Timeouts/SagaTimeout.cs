namespace Aggregates.Sagas;

/// <summary>
/// A command a saga plans for later. Yield it from <see cref="ISaga{TSagaState,TEvent}.ReactAsync"/>
/// among the commands: it is stored in the saga stream together with the saga's new state, and once
/// <see cref="Due"/> has passed, <see cref="Command"/> is executed like any other command. Execution
/// is at least once and can be late, so the command decides from the state of its aggregate whether
/// it still applies — for example, a reminder for a bill that has been paid in the meantime does nothing.
/// </summary>
/// <remarks>
/// The command waits in the store until it is due, so the storage integration's <c>Serialize</c> and
/// <c>Deserialize</c> must know its type under a stable name.
/// </remarks>
public sealed record SagaTimeout : ICommand {
    /// <summary>
    /// Initializes a new <see cref="SagaTimeout"/>.
    /// </summary>
    /// <param name="due">When the command is executed. A time in the past executes it right away.</param>
    /// <param name="command">The command to execute.</param>
    /// <exception cref="ArgumentException"><paramref name="command"/> is a <see cref="SagaTimeout"/>.</exception>
    public SagaTimeout(DateTimeOffset due, ICommand command) {
        ArgumentNullException.ThrowIfNull(command);
        if (command is SagaTimeout)
            throw new ArgumentException("A timeout can't hold another timeout.", nameof(command));
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

    /// <summary>
    /// Plans <paramref name="command"/> for <paramref name="due"/>.
    /// </summary>
    /// <param name="due">When the command is executed.</param>
    /// <param name="command">The command to execute.</param>
    public static SagaTimeout At(DateTimeOffset due, ICommand command) => new(due, command);
}
