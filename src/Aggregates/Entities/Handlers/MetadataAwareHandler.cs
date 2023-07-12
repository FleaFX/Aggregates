using Aggregates.Metadata;
using Aggregates.Types;
using System.Reflection;

namespace Aggregates.Entities.Handlers;

class MetadataAwareHandler<TCommand, TState, TEvent> : ICommandHandler<TCommand, TState, TEvent>
    where TCommand : ICommand<TCommand, TState, TEvent>
    where TState : IState<TState, TEvent> {
    readonly UnitOfWorkAwareHandler<TCommand, TState, TEvent> _handler;

    /// <summary>
    /// Initializes a new <see cref="MetadataAwareHandler{TCommand,TState,TEvent}"/>.
    /// </summary>
    /// <param name="handler">The <see cref="UnitOfWorkAwareHandler{TCommand,TState,TEvent}"/> to delegate to.</param>
    public MetadataAwareHandler(UnitOfWorkAwareHandler<TCommand, TState, TEvent> handler) =>
        _handler = handler;

    /// <summary>
    /// Asynchronously handles the given <paramref name="command"/>.
    /// </summary>
    /// <param name="command">The command object to handle.</param>
    /// <returns>A <see cref="ValueTask"/> that represents the asynchronous operation.</returns>
    public async ValueTask HandleAsync(TCommand command) {
        await using var scope = new MetadataScope();

        // commands may provide a context for metadata, they should be attributed with the MetadataAttributes to achieve this
        foreach (var metadata in command.GetType().GetCustomAttributes<MetadataAttribute>())
            scope.Add(metadata.Create(command));

        await _handler.HandleAsync(command);
    }
}