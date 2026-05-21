namespace Aggregates;

/// <summary>
/// Decorates an <see cref="ICommandHandler{TCommand}"/> with automatic retry on
/// <see cref="ConcurrencyException"/>. Each retry re-executes the full handler, so the
/// handler should build a fresh <see cref="UnitOfWorkScope"/> — and therefore a clean
/// <see cref="UnitOfWork"/> — on every call.
/// </summary>
/// <typeparam name="TCommand">The type of command being handled.</typeparam>
sealed class RetryCommandHandler<TCommand>(UnitOfWorkAwareCommandHandler<TCommand> inner, int maxAttempts = 3) : ICommandHandler<TCommand>
    where TCommand : ICommand {

    /// <inheritdoc/>
    public async ValueTask HandleAsync(TCommand command, CancellationToken cancellationToken = default) {
        var attempt = 0;
        while (true) {
            try {
                await inner.HandleAsync(command, cancellationToken);
                return;
            } catch (ConcurrencyException) when (++attempt < maxAttempts) {
                // The inner handler's UnitOfWorkScope.DisposeAsync already cleared the
                // UnitOfWork (via the try/finally in CommitAndClearAsync), so the next
                // attempt starts with a clean slate.
            }
        }
    }
}
