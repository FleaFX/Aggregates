﻿namespace Aggregates.Entities.Handlers;

/// <summary>
/// Handler that commits changes tracked by the given <see cref="UnitOfWork"/>.
/// </summary>
class UnitOfWorkAwareHandler<TCommand, TState, TEvent> : ICommandHandler<TCommand, TState, TEvent>
    where TCommand : ICommand<TState, TEvent>
    where TState : IState<TState, TEvent> {
    readonly UnitOfWork _unitOfWork;
    readonly EntityCommitDelegate _commitDelegate;
    readonly ICommandHandler<TCommand, TState, TEvent> _handler;

    /// <summary>
    /// Initializes a new <see cref="UnitOfWorkAwareHandler{TCommand,TState,TEvent}"/>.
    /// </summary>
    /// <param name="unitOfWork">The <see cref="UnitOfWork"/> that tracks changes.</param>
    /// <param name="commitDelegate">The <see cref="EntityCommitDelegate"/> that commits the changes made.</param>
    /// <param name="handlerFactory">Provides the handler that performs the actual work.</param>
    public UnitOfWorkAwareHandler(UnitOfWork unitOfWork, EntityCommitDelegate commitDelegate, ICommandHandlerFactory handlerFactory) {
        _unitOfWork = unitOfWork ?? throw new ArgumentNullException(nameof(unitOfWork));
        _commitDelegate = commitDelegate ?? throw new ArgumentNullException(nameof(commitDelegate));
        _handler = handlerFactory.Create<TCommand, TState, TEvent>();
    }

    /// <summary>
    /// Asynchronously handles the given <paramref name="command"/>.
    /// </summary>
    /// <param name="command">The command object to handle.</param>
    /// <returns>A <see cref="ValueTask"/> that represents the asynchronous operation.</returns>
    public async ValueTask HandleAsync(TCommand command) {
        await using var scope = new UnitOfWorkScope(_unitOfWork, _commitDelegate);
        await _handler.HandleAsync(command);
        scope.Complete();
    }
}