namespace Aggregates;

/// <summary>
/// Dispatches commands to their registered <see cref="ICommandHandler{TCommand}"/>.
/// </summary>
public interface ICommandDispatcher {
    /// <summary>
    /// Dispatches <paramref name="command"/> to its registered handler. The concrete runtime
    /// type of <paramref name="command"/> is used to resolve the handler, so callers may pass
    /// commands typed as <see cref="ICommand"/> without losing type information.
    /// </summary>
    ValueTask DispatchAsync(ICommand command, CancellationToken cancellationToken = default);
}
