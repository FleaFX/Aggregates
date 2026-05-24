namespace Aggregates;

/// <summary>
/// Decorates an <see cref="ICommandHandler{TCommand}"/> with <see cref="MetadataScope"/>
/// lifecycle management. Opens a new scope for each command execution, seeding it from any
/// ambient parent scope (e.g. a saga-level scope carrying correlation or causation identifiers).
/// The scope is available to <see cref="EntityRoot{TState,TEvent}.AcceptAsync"/> and to
/// application code via <see cref="MetadataScope.Current"/> for the duration of the command.
/// </summary>
/// <remarks>
/// Place this decorator inside <see cref="RetryCommandHandler{TCommand}"/> so that each retry
/// attempt starts with a fresh scope, preventing metadata accumulated during a failed attempt
/// from leaking into the retry.
/// </remarks>
/// <typeparam name="TCommand">The type of command being handled.</typeparam>
sealed class MetadataAwareCommandHandler<TCommand>(UnitOfWorkAwareCommandHandler<TCommand> inner) : ICommandHandler<TCommand>
    where TCommand : ICommand {

    /// <inheritdoc/>
    public async ValueTask HandleAsync(TCommand command, CancellationToken cancellationToken = default) {
        // Seed the new scope from the parent so that correlation/causation identifiers
        // propagated by an outer saga or policy scope are inherited automatically.
        var seed = MetadataScope.Current?.Snapshot();
        await using var scope = seed is not null
            ? new MetadataScope(seed)
            : new MetadataScope();
        await inner.HandleAsync(command, cancellationToken);
    }
}
