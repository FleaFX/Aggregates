using Aggregates.Types;

namespace Aggregates.Entities.CommandHandlers;

/// <summary>
/// Handler that decides whether a given command creates a new or affects an existing <see cref="EntityRoot{TState,TEvent}"/> object and acts accordingly.
/// </summary>c
class DefaultHandler<TCommand, TState, TEvent> : ICommandHandler<TCommand, TState, TEvent>
    where TCommand : ICommand<TCommand, TState, TEvent>
    where TState : IState<TState, TEvent> {
    readonly ICommandHandler<TCommand, TState, TEvent> _handler;

    /// <summary>
    /// Initializes a new <see cref="DefaultHandler{TCommand,TState,TEvent}"/>.
    /// </summary>
    /// <param name="creationHandler">The <see cref="ICommandHandler{TCommand,TState,TEvent}"/> to invoke when the handled command is the initial command for an aggregate (marked by <see cref="IInitialCommand"/>.)</param>
    /// <param name="modificationHandler">The <see cref="ICommandHandler{TCommand,TState,TEvent}"/> to invoke when the handled command is not the initial command for an aggregate.</param>
    /// <param name="markerInterfaceTypeProvider">Provides the type of the marker interface to look for on commands.</param>
    public DefaultHandler(
        CreationHandler<TCommand, TState, TEvent> creationHandler,
        ModificationHandler<TCommand, TState, TEvent> modificationHandler,
        MarkerInterfaceTypeProviderDelegate markerInterfaceTypeProvider) {
        _handler = typeof(TCommand).IsAssignableTo(markerInterfaceTypeProvider()) ? creationHandler : modificationHandler;
    }

    /// <summary>
    /// Asynchronously handles the given <paramref name="command"/>.
    /// </summary>
    /// <param name="command">The command object to handle.</param>
    /// <returns>A <see cref="ValueTask"/> that represents the asynchronous operation.</returns>
    public ValueTask HandleAsync(TCommand command) =>
        _handler.HandleAsync(command);
}