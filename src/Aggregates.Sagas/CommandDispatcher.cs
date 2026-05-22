using Microsoft.Extensions.DependencyInjection;

namespace Aggregates.Sagas;

/// <summary>
/// Resolves <see cref="ICommandHandler{TCommand}"/> from the DI container and delegates to it.
/// </summary>
sealed class CommandDispatcher(IServiceProvider serviceProvider) : ICommandDispatcher {
    /// <inheritdoc/>
    public ValueTask DispatchAsync<TCommand>(TCommand command, CancellationToken cancellationToken = default)
        where TCommand : ICommand =>
        serviceProvider.GetRequiredService<ICommandHandler<TCommand>>().HandleAsync(command, cancellationToken);
}
