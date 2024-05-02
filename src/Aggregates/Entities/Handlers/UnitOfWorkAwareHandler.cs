namespace Aggregates.Entities.Handlers;

/// <summary>
/// Handler that commits changes tracked by the given <see cref="UnitOfWork"/>.
/// </summary>
/// <remarks>
/// Initializes a new <see cref="UnitOfWorkAwareHandler{TCommand,TState,TEvent}"/>.
/// </remarks>
/// <param name="unitOfWork">The <see cref="UnitOfWork"/> that tracks changes.</param>
/// <param name="commitDelegate">The <see cref="EntityCommitDelegate"/> that commits the changes made.</param>
class UnitOfWorkAwareHandler<TCommand, TState, TEvent>(UnitOfWork unitOfWork, EntityCommitDelegate commitDelegate, ICommandHandlerFactory handlerFactory) : ICommandHandler<TCommand, TState, TEvent>
    where TCommand : ICommand<TState, TEvent>
    where TState : IState<TState, TEvent> {
    readonly ICommandHandler<TCommand, TState, TEvent> _handler = handlerFactory.Create<TCommand, TState, TEvent>();

    /// <summary>
    /// Asynchronously handles the given <paramref name="command"/>.
    /// </summary>
    /// <param name="command">The command object to handle.</param>
    /// <returns>A <see cref="ValueTask"/> that represents the asynchronous operation.</returns>
    public async ValueTask HandleAsync(TCommand command) {
        await using var scope = new UnitOfWorkScope(unitOfWork, commitDelegate);
        await _handler.HandleAsync(command);
        scope.Complete();
    }
}