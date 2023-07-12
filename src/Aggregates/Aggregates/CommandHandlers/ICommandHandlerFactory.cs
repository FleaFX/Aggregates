using Aggregates.Types;

namespace Aggregates.Aggregates.CommandHandlers;

interface ICommandHandlerFactory {
    ICommandHandler<TCommand, TState, TEvent> Create<TCommand, TState, TEvent>()
        where TCommand : ICommand<TCommand, TState, TEvent>
        where TState : IState<TState, TEvent>;
}