using Aggregates.Types;

namespace Aggregates.Aggregates.CommandHandlers;

/// <summary>
/// Handlers that first tries to retrieve an <see cref="AggregateRoot{TState,TEvent}"/> object, and if it doesn't exist yet, adds it to the repository.
/// </summary>
class GetOrAddHandler<TCommand, TState, TEvent> : ICommandHandler<TCommand, TState, TEvent>
    where TCommand : ICommand<TCommand, TState, TEvent>
    where TState : IState<TState, TEvent> {
    readonly IRepository<TState, TEvent> _repository;

    /// <summary>
    /// Initializes a new <see cref="GetOrAddHandler{TCommand,TState,TEvent}"/>.
    /// </summary>
    /// <param name="repository">The <see cref="IRepository{TState,TEvent}"/> to use when interacting with the aggregate which is affected by the handled command.</param>
    public GetOrAddHandler(IRepository<TState, TEvent> repository) =>
        _repository = repository ?? throw new ArgumentNullException(nameof(repository));

    /// <summary>
    /// Asynchronously handles the given <paramref name="command"/>.
    /// </summary>
    /// <param name="command">The command object to handle.</param>
    /// <returns>A <see cref="ValueTask"/> that represents the asynchronous operation.</returns>
    public async ValueTask HandleAsync(TCommand command) {
        var aggregateRoot = await _repository.TryGetAggregateRootAsync(command);
        if (aggregateRoot is null) {
            aggregateRoot = new AggregateRoot<TState, TEvent>(TState.Initial, AggregateVersion.None);
            _repository.Add(command, aggregateRoot);
        }
        await aggregateRoot.AcceptAsync(command);
    }
}