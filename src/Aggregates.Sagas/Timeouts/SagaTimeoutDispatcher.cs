using Aggregates.Subscriptions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace Aggregates.Sagas;

/// <summary>
/// Executes a due timeout: dispatches its command in a new scope, within a metadata scope seeded
/// with the metadata of the request, with retries and parking like a subscription. A timeout whose
/// command could not be deserialized is parked right away.
/// </summary>
sealed partial class SagaTimeoutDispatcher(
    IServiceScopeFactory scopeFactory,
    SubscriptionRetryPolicy retryPolicy,
    IParkedMessageSink parkedMessageSink,
    ILogger<SagaTimeoutDispatcher> logger) {

    /// <summary>
    /// Executes <paramref name="timeout"/>; failures are retried and parked under <paramref name="subscriptionId"/>.
    /// </summary>
    /// <param name="subscriptionId">The id of the timeout scheduler (<c>timeouts-{saga id}</c>).</param>
    /// <param name="timeout">The due timeout.</param>
    /// <param name="cancellationToken">A cancellation token.</param>
    public async ValueTask DeliverAsync(string subscriptionId, OpenSagaTimeout timeout, CancellationToken cancellationToken) {
        var message = new SubscriptionMessage(timeout.Command, timeout.CommitPosition, timeout.Request, timeout.Metadata);
        if (timeout.Command is not { } command) {
            // Deserializing gives the same result every time: retrying is pointless.
            var error = timeout.Error ?? new InvalidOperationException($"Timeout {timeout.Request} holds no command.");
            LogParkingUndeserializable(logger, error, timeout.Request, timeout.Request.StreamId);
            await parkedMessageSink.ParkAsync(subscriptionId, message, error, cancellationToken);
            return;
        }

        LogExecuting(logger, timeout.Request, timeout.Request.StreamId, command.GetType().Name);
        await retryPolicy.ExecuteAsync(async ct => {
            await using var scope = scopeFactory.CreateAsyncScope();
            // The metadata of the request, so that the command carries the correlation of the event that planned it.
            await using var metadataScope = new MetadataScope(timeout.Metadata);
            await scope.ServiceProvider.GetRequiredService<ICommandDispatcher>().DispatchAsync(command, ct);
        }, subscriptionId, message, cancellationToken);
    }

    [LoggerMessage(Level = LogLevel.Debug, Message = "Executing timeout {Request} for saga {SagaId}: {CommandType}")]
    static partial void LogExecuting(ILogger logger, EventReference request, string sagaId, string commandType);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Parking timeout {Request} for saga {SagaId}: its command can't be deserialized")]
    static partial void LogParkingUndeserializable(ILogger logger, Exception exception, EventReference request, string sagaId);
}
