namespace Aggregates;

/// <summary>
/// Abstract base class for command handlers. Derive from this class to implement a custom
/// handler, or let assembly scanning register
/// <see cref="CommandHandler{TCommand,TState,TEvent}"/> automatically via
/// <see cref="AggregatesOptions.ScanAssemblies"/>.
/// </summary>
/// <typeparam name="TCommand">The type of command to handle.</typeparam>
abstract class CommandHandler<TCommand> : ICommandHandler<TCommand>
    where TCommand : ICommand {

    /// <inheritdoc/>
    public abstract ValueTask HandleAsync(TCommand command, CancellationToken cancellationToken = default);
}

/// <summary>
/// Default command handler that loads the target aggregate via
/// <see cref="IRepository{TState,TEvent}"/>, applies the command, and lets the ambient
/// <see cref="UnitOfWork"/> collect the resulting events. When no aggregate exists for
/// <see cref="ICommand{TState,TEvent}.Id"/>, a new one is created automatically.
/// </summary>
/// <typeparam name="TCommand">The type of command to handle.</typeparam>
/// <typeparam name="TState">The type of the aggregate state.</typeparam>
/// <typeparam name="TEvent">The type of the domain events.</typeparam>
class CommandHandler<TCommand, TState, TEvent>(IRepository<TState, TEvent> repository) : CommandHandler<TCommand>
    where TCommand : ICommand<TState, TEvent>
    where TState : IState<TState, TEvent> {

    /// <inheritdoc/>
    public override async ValueTask HandleAsync(TCommand command, CancellationToken cancellationToken = default) {
        var aggregateRoot = await repository.TryGetEntityRootAsync(command.Id, cancellationToken);
        if (aggregateRoot is null) {
            aggregateRoot = new EntityRoot<TState, TEvent>(AggregateVersion.None, TState.Initial);
            repository.Add(command.Id, aggregateRoot);
        }
        await aggregateRoot.AcceptAsync(command, cancellationToken);
    }
}
