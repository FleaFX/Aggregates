namespace Aggregates.Sagas;

/// <summary>
/// Dispatches commands produced by a saga handler after
/// <see cref="ISaga{TSagaState,TEvent}.ReactAsync"/> completes.
/// </summary>
public interface ICommandDispatcher {
    /// <summary>
    /// Dispatches <paramref name="command"/> to its registered <see cref="ICommandHandler{TCommand}"/>.
    /// </summary>
    /// <typeparam name="TCommand">The type of the command to dispatch.</typeparam>
    /// <param name="command">The command to dispatch.</param>
    /// <param name="cancellationToken">A cancellation token.</param>
    ValueTask DispatchAsync<TCommand>(TCommand command, CancellationToken cancellationToken = default)
        where TCommand : ICommand;
}
