using System.Reflection;
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
        ArgumentNullException.ThrowIfNull(command);
        var commandType = command.GetType();
        var handlerType = typeof(ICommandHandler<>).MakeGenericType(commandType);
        object handler;
        try {
            handler = serviceProvider.GetRequiredService(handlerType);
        } catch (InvalidOperationException exception) when (!HasHandler(commandType)) {
            throw new InvalidOperationException(command is DeferredCommand
                ? "A DeferredCommand is executed by the scheduler of a saga or policy: yield it from ReactAsync instead of dispatching it."
                : $"No handler for {commandType.Name}: it implements no ICommand<TState, TEvent>, or it was not passed to AddAggregates with ScanAssemblies or ScanTypes.",
                exception);
        }
        return (ValueTask)handlerType
            .GetMethod(nameof(ICommandHandler<ICommand>.HandleAsync))!
            .Invoke(handler, BindingFlags.DoNotWrapExceptions, binder: null, [command, cancellationToken], culture: null)!;
    }

    // Whether AddAggregates registered the handler of the command; without it the decorator chain can't be built.
    bool HasHandler(Type commandType) =>
        serviceProvider.GetService<IServiceProviderIsService>()?.IsService(typeof(CommandHandler<>).MakeGenericType(commandType)) ?? true;
}
