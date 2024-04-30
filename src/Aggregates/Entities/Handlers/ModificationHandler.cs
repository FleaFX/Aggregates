namespace Aggregates.Entities.Handlers;

/// <summary>
/// Handler that retrieves an existing <see cref="EntityRoot{TState,TEvent}"/> object from the repository and uses the given command to affect its state.
/// </summary>
/// <typeparam name="TCommand">The type of the command that affects the state of the aggregate.</typeparam>
/// <typeparam name="TState">The type of the maintained state object.</typeparam>
/// <typeparam name="TEvent">The type of the event(s) that are applicable.</typeparam>
/// <remarks>
/// Initializes a new <see cref="ModificationHandler{TCommand,TState,TEvent}"/>.
/// </remarks>
/// <param name="repository">The <see cref="IRepository{TState,TEvent}"/> to use when retrieving the aggregate which is affected by the handled command.</param>
class ModificationHandler<TCommand, TState, TEvent>(IRepository<TState, TEvent> repository) : ICommandHandler<TCommand, TState, TEvent>
    where TCommand : ICommand<TState, TEvent>
    where TState : IState<TState, TEvent> {
    readonly IRepository<TState, TEvent> _repository = repository ?? throw new ArgumentNullException(nameof(repository));

    /// <summary>
    /// Asynchronously handles the given <paramref name="command"/>.
    /// </summary>
    /// <param name="command">The command object to handle.</param>
    /// <returns>A <see cref="ValueTask"/> that represents the asynchronous operation.</returns>
    public async ValueTask HandleAsync(TCommand command) {
        var aggregateRoot = await _repository.GetEntityRootAsync(command.Id);
        await aggregateRoot.AcceptAsync(command);
    }
}