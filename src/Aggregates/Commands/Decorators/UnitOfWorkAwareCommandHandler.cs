namespace Aggregates;

/// <summary>
/// Decorates a <see cref="CommandHandler{TCommand}"/> with <see cref="UnitOfWork"/> lifecycle
/// management. Creates a fresh <see cref="UnitOfWork"/> and <see cref="UnitOfWorkScope"/> for each
/// command, making them available to repositories via the ambient scope. Calls
/// <see cref="UnitOfWorkScope.Complete"/> after the inner handler returns successfully, triggering
/// the <paramref name="commitDelegate"/> on disposal.
/// </summary>
/// <remarks>
/// Place this decorator inside <see cref="RetryCommandHandler{TCommand}"/> so that each retry
/// attempt starts with a clean <see cref="UnitOfWork"/>.
/// </remarks>
/// <typeparam name="TCommand">The type of command being handled.</typeparam>
sealed class UnitOfWorkAwareCommandHandler<TCommand>(CommandHandler<TCommand> inner, CommitDelegate commitDelegate) : ICommandHandler<TCommand>
    where TCommand : ICommand {

    /// <inheritdoc/>
    public async ValueTask HandleAsync(TCommand command, CancellationToken cancellationToken = default) {
        await using var scope = new UnitOfWorkScope(new UnitOfWork(), commitDelegate);
        await inner.HandleAsync(command, cancellationToken);
        scope.Complete();
    }
}
