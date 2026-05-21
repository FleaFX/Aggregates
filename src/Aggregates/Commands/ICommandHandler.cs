namespace Aggregates;

/// <summary>
/// Handles a command by loading the target aggregate, applying the command, and persisting the result.
/// </summary>
/// <typeparam name="TCommand">The type of the command to handle.</typeparam>
public interface ICommandHandler<in TCommand> where TCommand : ICommand {
    /// <summary>
    /// Handles <paramref name="command"/>.
    /// </summary>
    /// <param name="command">The command to handle.</param>
    /// <param name="cancellationToken">A cancellation token.</param>
    ValueTask HandleAsync(TCommand command, CancellationToken cancellationToken = default);
}
