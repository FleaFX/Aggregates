using Microsoft.Extensions.Logging;

namespace Aggregates;

/// <summary>
/// Decorates an <see cref="ICommandHandler{TCommand}"/> with structured logging. Logs at
/// <see cref="LogLevel.Debug"/> when handling starts and succeeds, and at
/// <see cref="LogLevel.Error"/> when the inner handler throws.
/// </summary>
/// <typeparam name="TCommand">The type of command being handled.</typeparam>
sealed partial class LoggingCommandHandler<TCommand>(RetryCommandHandler<TCommand> inner, ILogger<LoggingCommandHandler<TCommand>> logger): ICommandHandler<TCommand>
    where TCommand : ICommand {

    /// <inheritdoc/>
    public async ValueTask HandleAsync(TCommand command, CancellationToken cancellationToken = default) {
        var commandType = typeof(TCommand).Name;
        LogHandling(logger, commandType);
        try {
            await inner.HandleAsync(command, cancellationToken);
            LogHandled(logger, commandType);
        } catch (Exception ex) {
            LogFailed(logger, ex, commandType);
            throw;
        }
    }

    [LoggerMessage(Level = LogLevel.Debug, Message = "Handling {CommandType}")]
    static partial void LogHandling(ILogger logger, string commandType);

    [LoggerMessage(Level = LogLevel.Debug, Message = "Handled {CommandType}")]
    static partial void LogHandled(ILogger logger, string commandType);

    [LoggerMessage(Level = LogLevel.Error, Message = "Failed to handle {CommandType}")]
    static partial void LogFailed(ILogger logger, Exception exception, string commandType);
}
