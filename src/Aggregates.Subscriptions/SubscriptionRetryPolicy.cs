using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace Aggregates.Subscriptions;

/// <summary>
/// Wraps a subscription handler invocation with retry-with-backoff and parked-message fallback.
/// </summary>
/// <remarks>
/// Inject as a singleton and share across all subscription services.
/// <list type="bullet">
///   <item>On success: returns normally.</item>
///   <item>On transient failure when attempts remain: waits with exponential backoff and retries.</item>
///   <item>On final failure: calls <see cref="IParkedMessageSink.ParkAsync"/> and returns normally,
///   allowing the subscription checkpoint to advance past this message. With
///   <see cref="SubscriptionErrorMode.Block"/> there is no final failure: it keeps retrying, with the
///   backoff capped at <see cref="SubscriptionErrorHandlingOptions.MaxDelay"/>, until an attempt
///   succeeds.</item>
///   <item><see cref="OperationCanceledException"/> is always propagated — it is never swallowed
///   or counted as a retry attempt.</item>
/// </list>
/// </remarks>
/// <param name="parkedMessageSink">Parks a message after its final failure.</param>
/// <param name="options">The number of attempts and the backoff.</param>
/// <param name="logger">Logs when a blocking subscription starts and stops retrying; optional.</param>
public sealed partial class SubscriptionRetryPolicy(
    IParkedMessageSink parkedMessageSink,
    SubscriptionErrorHandlingOptions options,
    ILogger<SubscriptionRetryPolicy>? logger = null
) {

    readonly ILogger _logger = (ILogger?)logger ?? NullLogger.Instance;

    /// <summary>
    /// Executes <paramref name="handler"/> with retry and park-on-failure semantics.
    /// </summary>
    /// <param name="handler">The handler to invoke, receiving a cancellation token.</param>
    /// <param name="subscriptionId">Subscription identifier, forwarded to the parked-message sink.</param>
    /// <param name="message">The message being processed, forwarded to the parked-message sink on failure.</param>
    /// <param name="cancellationToken">A cancellation token.</param>
    public ValueTask ExecuteAsync(Func<CancellationToken, ValueTask> handler, string subscriptionId, SubscriptionMessage message, CancellationToken cancellationToken) =>
        ExecuteAsync(handler, subscriptionId, message, SubscriptionErrorMode.Park, cancellationToken);

    /// <summary>
    /// Executes <paramref name="handler"/> with retries; after the final failure it parks the message,
    /// or, with <see cref="SubscriptionErrorMode.Block"/>, keeps retrying until an attempt succeeds.
    /// </summary>
    /// <param name="handler">The handler to invoke, receiving a cancellation token.</param>
    /// <param name="subscriptionId">Subscription identifier, forwarded to the parked-message sink.</param>
    /// <param name="message">The message being processed, forwarded to the parked-message sink on failure.</param>
    /// <param name="errorMode">What to do after the final failure.</param>
    /// <param name="cancellationToken">A cancellation token.</param>
    public async ValueTask ExecuteAsync(
        Func<CancellationToken, ValueTask> handler,
        string subscriptionId,
        SubscriptionMessage message,
        SubscriptionErrorMode errorMode,
        CancellationToken cancellationToken
    ) {
        var maxRetries = Math.Max(options.MaxRetries, 1);
        for (var attempt = 1; ; attempt++) {
            try {
                await handler(cancellationToken);
                if (attempt > maxRetries)
                    LogUnblocked(_logger, subscriptionId, message.CommitPosition, attempt);
                return;
            } catch (OperationCanceledException) {
                throw;
            } catch (Exception exception) when (attempt < maxRetries || errorMode == SubscriptionErrorMode.Block) {
                if (attempt == maxRetries)
                    LogBlocked(_logger, exception, subscriptionId, message.CommitPosition, attempt);
                await Task.Delay(
                    Backoff.Delay(attempt, options.InitialDelay, options.MaxDelay, options.BackoffMultiplier),
                    cancellationToken);
            } catch (Exception ex) {
                await parkedMessageSink.ParkAsync(subscriptionId, message, ex, cancellationToken);
                return;
            }
        }
    }

    /// <summary>
    /// Executes <paramref name="handler"/> with the same retries as <see cref="SubscriptionErrorMode.Park"/>,
    /// but returns the exception of the final attempt instead of parking. Used to replay a parked message.
    /// </summary>
    /// <param name="handler">The handler to invoke, receiving a cancellation token.</param>
    /// <param name="cancellationToken">A cancellation token.</param>
    /// <returns>The exception of the final attempt, or <see langword="null"/> when an attempt succeeded.</returns>
    public async ValueTask<Exception?> TryExecuteAsync(Func<CancellationToken, ValueTask> handler, CancellationToken cancellationToken) {
        var maxRetries = Math.Max(options.MaxRetries, 1);
        for (var attempt = 1; ; attempt++) {
            try {
                await handler(cancellationToken);
                return null;
            } catch (OperationCanceledException) {
                throw;
            } catch (Exception) when (attempt < maxRetries) {
                await Task.Delay(
                    Backoff.Delay(attempt, options.InitialDelay, options.MaxDelay, options.BackoffMultiplier),
                    cancellationToken);
            } catch (Exception ex) {
                return ex;
            }
        }
    }

    [LoggerMessage(Level = LogLevel.Error, Message = "Subscription '{SubscriptionId}' failed {Attempts} times on the message at position {Position}; it blocks and retries the message until it succeeds")]
    static partial void LogBlocked(ILogger logger, Exception exception, string subscriptionId, ulong position, int attempts);

    [LoggerMessage(Level = LogLevel.Information, Message = "Subscription '{SubscriptionId}' handled the message at position {Position} after {Attempts} attempts; it no longer blocks")]
    static partial void LogUnblocked(ILogger logger, string subscriptionId, ulong position, int attempts);
}
