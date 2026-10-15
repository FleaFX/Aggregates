using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace Aggregates.Subscriptions;

/// <summary>
/// Executes a due deferred command: dispatches it in a new scope, within a metadata scope seeded
/// with the metadata of the request, with retries and parking like a subscription. Parks a request
/// that can't be delivered right away: a command or deadline payload that could not be deserialized,
/// or a saga deadline its scheduler doesn't deliver.
/// </summary>
sealed partial class DeferredCommandDispatcher(
    IServiceScopeFactory scopeFactory,
    SubscriptionRetryPolicy retryPolicy,
    IParkedMessageSink parkedMessageSink,
    ILogger logger
) {

    /// <summary>
    /// Executes <paramref name="request"/>; failures are retried and parked under <paramref name="subscriptionId"/>.
    /// A request without a command is parked right away.
    /// </summary>
    /// <param name="subscriptionId">The id of the scheduler (<c>scheduled-{id}</c>).</param>
    /// <param name="request">The due request.</param>
    /// <param name="cancellationToken">A cancellation token.</param>
    public async ValueTask DeliverAsync(string subscriptionId, ScheduledRequest request, CancellationToken cancellationToken) {
        if (request.Payload is not ICommand command) {
            // Deserializing gives the same result every time: retrying is pointless.
            await ParkAsync(subscriptionId, request, request.Error ?? new InvalidOperationException($"Request {request.Request} holds no command."), cancellationToken);
            return;
        }

        LogExecuting(logger, request.Request, request.Request.StreamId, command.GetType().Name);
        var message = new SubscriptionMessage(command, request.CommitPosition, request.Request, request.Metadata);
        await retryPolicy.ExecuteAsync(async ct => {
            await using var scope = scopeFactory.CreateAsyncScope();
            // The metadata of the request, so that the command carries the correlation of the event that deferred it.
            await using var metadataScope = new MetadataScope(request.Metadata);
            await scope.ServiceProvider.GetRequiredService<ICommandDispatcher>().DispatchAsync(command, ct);
        }, subscriptionId, message, cancellationToken);
    }

    /// <summary>
    /// Parks <paramref name="request"/> under <paramref name="subscriptionId"/> without delivering it.
    /// </summary>
    /// <param name="subscriptionId">The id of the scheduler (<c>scheduled-{id}</c>).</param>
    /// <param name="request">The due request.</param>
    /// <param name="error">Why it can't be delivered.</param>
    /// <param name="cancellationToken">A cancellation token.</param>
    public async ValueTask ParkAsync(string subscriptionId, ScheduledRequest request, Exception error, CancellationToken cancellationToken) {
        LogParking(logger, error, request.Kind == ScheduledKind.Deadline ? "saga deadline" : "deferred command", request.Request, request.Request.StreamId);
        var message = new SubscriptionMessage(request.Payload, request.CommitPosition, request.Request, request.Metadata);
        await parkedMessageSink.ParkAsync(subscriptionId, message, error, cancellationToken);
    }

    [LoggerMessage(Level = LogLevel.Debug, Message = "Executing deferred command {Request} from {Stream}: {CommandType}")]
    static partial void LogExecuting(ILogger logger, EventReference request, string stream, string commandType);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Parking {Kind} {Request} from {Stream} without delivering it")]
    static partial void LogParking(ILogger logger, Exception exception, string kind, EventReference request, string stream);
}
