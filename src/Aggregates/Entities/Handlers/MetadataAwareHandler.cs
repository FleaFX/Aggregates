using Aggregates.Metadata;
using System.Reflection;

namespace Aggregates.Entities.Handlers;

/// <summary>
/// Initializes a new <see cref="MetadataAwareHandler{TCommand,TState,TEvent}"/>.
/// </summary>
/// <param name="handler">The <see cref="UnitOfWorkAwareHandler{TCommand,TState,TEvent}"/> to delegate to.</param>
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
        
        // commands may provide a context for metadata, they should be attributed with the MetadataAttributes to achieve this
        // we're requesting the command to create the metadata AFTER it's been handled, because the command might fail and any
        // metadata might be meaningless in that case and no events would be stored anyway
        foreach (var metadata in command.GetType().GetCustomAttributes<MetadataAttribute>())
            scope.Add(metadata.Create(command));
    }
}