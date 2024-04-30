namespace Aggregates.Entities.Handlers;

/// <summary>
/// Handlers that first tries to retrieve an <see cref="EntityRoot{TState,TEvent}"/> object, and if it doesn't exist yet, adds it to the repository.
/// </summary>
/// <remarks>
/// Initializes a new <see cref="GetOrAddHandler{TCommand,TState,TEvent}"/>.
/// </remarks>
/// <param name="repository">The <see cref="IRepository{TState,TEvent}"/> to use when interacting with the aggregate which is affected by the handled command.</param>
class GetOrAddHandler<TCommand, TState, TEvent>(IRepository<TState, TEvent> repository) : ICommandHandler<TCommand, TState, TEvent>
    where TCommand : ICommand<TState, TEvent>
    where TState : IState<TState, TEvent> {
    readonly IRepository<TState, TEvent> _repository = repository ?? throw new ArgumentNullException(nameof(repository));

    /// <summary>
    /// Asynchronously handles the given <paramref name="command"/>.
    /// </summary>
    /// <param name="command">The command object to handle.</param>
    /// <returns>A <see cref="ValueTask"/> that represents the asynchronous operation.</returns>
    public async ValueTask HandleAsync(TCommand command) {
        var aggregateRoot = await _repository.TryGetEntityRootAsync(command.Id);
        if (aggregateRoot is null) {
            aggregateRoot = new EntityRoot<TState, TEvent>(TState.Initial, AggregateVersion.None);
            _repository.Add(command.Id, aggregateRoot);
        }
        await aggregateRoot.AcceptAsync(command);
    }
}