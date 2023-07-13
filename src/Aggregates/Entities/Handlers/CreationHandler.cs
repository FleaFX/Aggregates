namespace Aggregates.Entities.Handlers;

/// <summary>
/// Handler that creates a new <see cref="EntityRoot{TState,TEvent}"/> object and adds it to the repository.
/// </summary>
/// <typeparam name="TCommand">The type of the command that affects the state of the aggregate.</typeparam>
/// <typeparam name="TState">The type of the maintained state object.</typeparam>
/// <typeparam name="TEvent">The type of the event(s) that are applicable.</typeparam>
class CreationHandler<TCommand, TState, TEvent> : ICommandHandler<TCommand, TState, TEvent>
    where TCommand : ICommand< TState, TEvent>
    where TState : IState<TState, TEvent> {
    readonly IRepository<TState, TEvent> _repository;

    /// <summary>
    /// Initializes a new <see cref="CreationHandler{TCommand,TState,TEvent}"/>.
    /// </summary>
    /// <param name="repository">The <see cref="IRepository{TState,TEvent}"/> to use when retrieving the aggregate which is affected by the handled command.</param>
    public CreationHandler(IRepository<TState, TEvent> repository) =>
        _repository = repository ?? throw new ArgumentNullException(nameof(repository));

    /// <summary>
    /// Asynchronously handles the given <paramref name="command"/>.
    /// </summary>
    /// <param name="command">The command object to handle.</param>
    /// <returns>A <see cref="ValueTask"/> that represents the asynchronous operation.</returns>
    public async ValueTask HandleAsync(TCommand command) {
        var aggregateRoot = new EntityRoot<TState, TEvent>(TState.Initial, AggregateVersion.None);
        await aggregateRoot.AcceptAsync(command);
        _repository.Add(command.Id, aggregateRoot);
    }
}