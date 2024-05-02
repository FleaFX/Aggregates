using Aggregates.Metadata;

namespace Aggregates.Entities.Handlers;

/// <summary>
/// Initializes a new <see cref="MetadataAwareHandler{TCommand,TState,TEvent}"/>.
/// </summary>
/// <param name="handlerFactory">Provides the <see cref="ICommandHandler{TCommand,TState,TEvent}"/> that performs the actual work.</param>
class MetadataAwareHandler<TCommand, TState, TEvent>(ICommandHandlerFactory handlerFactory) : ICommandHandler<TCommand, TState, TEvent>
    where TCommand : ICommand<TState, TEvent>
    where TState : IState<TState, TEvent> {
    readonly ICommandHandler<TCommand, TState, TEvent> _handler = handlerFactory.Create<TCommand, TState, TEvent>();

    /// <summary>
    /// Asynchronously handles the given <paramref name="command"/>.
    /// </summary>
    /// <param name="command">The command object to handle.</param>
    /// <returns>A <see cref="ValueTask"/> that represents the asynchronous operation.</returns>
    public async ValueTask HandleAsync(TCommand command) {
        await using var scope = new MetadataScope();
        
        await _handler.HandleAsync(command);
    }
}