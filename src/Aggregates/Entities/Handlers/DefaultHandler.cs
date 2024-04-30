namespace Aggregates.Entities.Handlers;

/// <summary>
/// Handler that decides whether a given command creates a new or affects an existing <see cref="EntityRoot{TState,TEvent}"/> object and acts accordingly.
/// </summary>c
/// <remarks>
/// Initializes a new <see cref="DefaultHandler{TCommand,TState,TEvent}"/>.
/// </remarks>
/// <param name="creationHandler">The <see cref="ICommandHandler{TCommand,TState,TEvent}"/> to invoke when the handled command is the initial command for an aggregate (marked by <see cref="IInitialCommand"/>.)</param>
/// <param name="modificationHandler">The <see cref="ICommandHandler{TCommand,TState,TEvent}"/> to invoke when the handled command is not the initial command for an aggregate.</param>
/// <param name="markerInterfaceTypeProvider">Provides the type of the marker interface to look for on commands.</param>
class DefaultHandler<TCommand, TState, TEvent>(
    CreationHandler<TCommand, TState, TEvent> creationHandler,
    ModificationHandler<TCommand, TState, TEvent> modificationHandler,
    MarkerInterfaceTypeProviderDelegate markerInterfaceTypeProvider) : ICommandHandler<TCommand, TState, TEvent>
    where TCommand : ICommand<TState, TEvent>
    where TState : IState<TState, TEvent> {
    readonly ICommandHandler<TCommand, TState, TEvent> _handler = typeof(TCommand).IsAssignableTo(markerInterfaceTypeProvider()) ? creationHandler : modificationHandler;

    /// <summary>
    /// Asynchronously handles the given <paramref name="command"/>.
    /// </summary>
    /// <param name="command">The command object to handle.</param>
    /// <returns>A <see cref="ValueTask"/> that represents the asynchronous operation.</returns>
    public ValueTask HandleAsync(TCommand command) =>
        _handler.HandleAsync(command);
}