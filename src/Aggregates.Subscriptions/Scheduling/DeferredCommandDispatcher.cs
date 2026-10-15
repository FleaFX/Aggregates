using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace Aggregates.Subscriptions;

/// <summary>
/// Executes a due request: dispatches its command in a new scope, within a metadata scope seeded
/// with the metadata of the request, with retries and parking like a subscription. A request whose
/// command could not be deserialized is parked right away.
/// </summary>
sealed partial class DeferredCommandDispatcher(
    IServiceScopeFactory scopeFactory,
    SubscriptionRetryPolicy retryPolicy,
    IParkedMessageSink parkedMessageSink,
    ILogger logger
) {

    /// <summary>
    /// Executes <paramref name="request"/>; failures are retried and parked under <paramref name="subscriptionId"/>.
    /// </summary>
    /// <param name="subscriptionId">The id of the scheduler (<c>scheduled-{id}</c>).</param>
    /// <param name="request">The due request.</param>
    /// <param name="cancellationToken">A cancellation token.</param>
    public async ValueTask DeliverAsync(string subscriptionId, ScheduledRequest request, CancellationToken cancellationToken) {
        var message = new SubscriptionMessage(request.Command, request.CommitPosition, request.Request, request.Metadata);
        if (request.Command is not { } command) {
            // Deserializing gives the same result every time: retrying is pointless.
            var error = request.Error ?? new InvalidOperationException($"Request {request.Request} holds no command.");
            LogParkingUndeserializable(logger, error, request.Request, request.Request.StreamId);
            await parkedMessageSink.ParkAsync(subscriptionId, message, error, cancellationToken);
            return;
        }

        LogExecuting(logger, request.Request, request.Request.StreamId, command.GetType().Name);
        await retryPolicy.ExecuteAsync(async ct => {
            await using var scope = scopeFactory.CreateAsyncScope();
            // The metadata of the request, so that the command carries the correlation of the event that deferred it.
            await using var metadataScope = new MetadataScope(request.Metadata);
            await scope.ServiceProvider.GetRequiredService<ICommandDispatcher>().DispatchAsync(command, ct);
        }, subscriptionId, message, cancellationToken);
    }

    [LoggerMessage(Level = LogLevel.Debug, Message = "Executing deferred command {Request} from {Stream}: {CommandType}")]
    static partial void LogExecuting(ILogger logger, EventReference request, string stream, string commandType);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Parking deferred command {Request} from {Stream}: it can't be deserialized")]
    static partial void LogParkingUndeserializable(ILogger logger, Exception exception, EventReference request, string stream);
}
