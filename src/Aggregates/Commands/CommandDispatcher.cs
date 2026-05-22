using Microsoft.Extensions.DependencyInjection;

namespace Aggregates;

/// <summary>
/// Resolves <see cref="ICommandHandler{TCommand}"/> from the DI container and delegates to it.
/// Uses reflection via the interface type so that the concrete command type is preserved even
/// when the caller only holds an <see cref="ICommand"/> reference.
/// </summary>
sealed class CommandDispatcher(IServiceProvider serviceProvider) : ICommandDispatcher {
    /// <inheritdoc/>
    public ValueTask DispatchAsync(ICommand command, CancellationToken cancellationToken = default) {
        var commandType = command.GetType();
        var handlerType = typeof(ICommandHandler<>).MakeGenericType(commandType);
        var handler = serviceProvider.GetRequiredService(handlerType);
        return (ValueTask)handlerType
            .GetMethod(nameof(ICommandHandler<ICommand>.HandleAsync))!
            .Invoke(handler, [command, cancellationToken])!;
    }
}
