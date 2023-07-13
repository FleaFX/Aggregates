namespace Aggregates;

/// <summary>
/// Marker interface for command handlers.
/// </summary>
/// <typeparam name="TCommand">The type of the command that affects the state of the aggregate.</typeparam>
/// <typeparam name="TState">The type of the maintained state object.</typeparam>
/// <typeparam name="TEvent">The type of the event(s) that are applicable.</typeparam>
public interface ICommandHandler<in TCommand, TState, TEvent>
    where TCommand : ICommand<TState, TEvent>
    where TState : IState<TState, TEvent> {
    /// <summary>
    /// Asynchronously handles the given <paramref name="command"/>.
    /// </summary>
    /// <param name="command">The command object to handle.</param>
    /// <returns>A <see cref="ValueTask"/> that represents the asynchronous operation.</returns>
    ValueTask HandleAsync(TCommand command);
}