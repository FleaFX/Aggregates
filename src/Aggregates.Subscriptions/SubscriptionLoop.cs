using Microsoft.Extensions.Logging;

namespace Aggregates.Subscriptions;

/// <summary>
/// Runs a subscription until the host stops: subscribes, hands every message to a processing
/// delegate, records checkpoints, and subscribes again after a transient failure.
/// </summary>
/// <remarks>
/// <list type="bullet">
///   <item>The first subscription starts after the stored checkpoint. After a failure, the loop
///   subscribes again after the last processed message, which can be ahead of the stored
///   checkpoint (see <see cref="SubscriptionCheckpointOptions"/>), so a reconnect delivers no
///   message twice.</item>
///   <item>Whether a failure is transient is decided by <see cref="ISubscriptionFactory.IsTransient"/>.
///   Transient failures, and a subscription that ends without failure, lead to a new subscription
///   after a delay as configured by <see cref="SubscriptionResubscribeOptions"/>. Any other
///   failure is logged and rethrown.</item>
///   <item>A message whose <see cref="SubscriptionMessage.DeserializationError"/> is set is parked
///   right away, without retries, and not handed to the processing delegate.</item>
///   <item>When the loop ends, the last processed position is written once more. A failure to
///   write it is logged and never replaces the exception that ended the loop.</item>
/// </list>
/// The loop holds no state between calls to <see cref="RunAsync"/>, so one instance can serve
/// every subscription.
/// </remarks>
public sealed partial class SubscriptionLoop(
    ISubscriptionFactory subscriptionFactory,
    ICheckpointStore checkpointStore,
    IParkedMessageSink parkedMessageSink,
    SubscriptionCheckpointOptions checkpointOptions,
    SubscriptionResubscribeOptions resubscribeOptions,
    TimeProvider timeProvider,
    ILogger<SubscriptionLoop> logger) {

    static readonly TimeSpan FinalFlushTimeout = TimeSpan.FromSeconds(5);

    // From this many consecutive failures on, a resubscribe is logged as an error instead of a warning.
    const int ErrorLevelFailures = 5;

    /// <summary>
    /// Runs the subscription until <paramref name="stoppingToken"/> is cancelled or a failure
    /// that is not transient occurs.
    /// </summary>
    /// <param name="subscriptionId">The identifier of the subscription, used for its checkpoint and parked messages.</param>
    /// <param name="startFromEnd">
    /// When <see langword="true"/> and there is no checkpoint yet, starts from the current end of the stream.
    /// </param>
    /// <param name="process">
    /// Handles one deserialized message. Exceptions it throws are treated as subscription failures;
    /// handler failures should be retried and parked within it, with <see cref="SubscriptionRetryPolicy"/>.
    /// </param>
    /// <param name="stoppingToken">Stops the loop.</param>
    public async Task RunAsync(
        string subscriptionId,
        bool startFromEnd,
        Func<SubscriptionMessage, CancellationToken, ValueTask> process,
        CancellationToken stoppingToken) {

        var tracker = new CheckpointTracker(checkpointStore, subscriptionId, checkpointOptions, timeProvider);
        var failures = 0;
        try {
            while (true) {
                var from = tracker.LastPosition;
                TimeSpan delay;
                try {
                    from ??= await checkpointStore.GetAsync(subscriptionId, stoppingToken);
                    LogSubscribing(logger, failures == 0 ? LogLevel.Information : LogLevel.Debug, subscriptionId, from, startFromEnd);

                    await using var subscription = subscriptionFactory.Subscribe(from, startFromEnd, stoppingToken);
                    await foreach (var message in subscription.WithCancellation(stoppingToken)) {
                        if (message.DeserializationError is { } error) {
                            LogParkingUndeserializable(logger, error, subscriptionId, message.CommitPosition);
                            await parkedMessageSink.ParkAsync(subscriptionId, message, error, stoppingToken);
                        } else {
                            await process(message, stoppingToken);
                        }

                        await tracker.AdvanceAsync(message.CommitPosition, stoppingToken);

                        if (failures > 0) {
                            LogResubscribed(logger, subscriptionId, from, failures);
                            failures = 0;
                        }
                    }

                    delay = NextDelay(++failures);
                    LogSubscriptionEnded(logger, subscriptionId, tracker.LastPosition ?? from, failures, delay);
                } catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested) {
                    return;
                } catch (Exception exception) when (subscriptionFactory.IsTransient(exception)) {
                    delay = NextDelay(++failures);
                    LogResubscribing(logger, failures >= ErrorLevelFailures ? LogLevel.Error : LogLevel.Warning,
                        exception, subscriptionId, tracker.LastPosition ?? from, failures, delay);
                } catch (Exception exception) {
                    LogSubscriptionFailed(logger, exception, subscriptionId, tracker.LastPosition ?? from);
                    throw;
                }

                try {
                    await Task.Delay(delay, timeProvider, stoppingToken);
                } catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested) {
                    return;
                }
            }
        } finally {
            await FlushFinalCheckpointAsync(tracker, subscriptionId);
        }
    }

    TimeSpan NextDelay(int failures) =>
        Backoff.Delay(failures, resubscribeOptions.InitialDelay, resubscribeOptions.MaxDelay, resubscribeOptions.BackoffMultiplier);

    async ValueTask FlushFinalCheckpointAsync(CheckpointTracker tracker, string subscriptionId) {
        try {
            // The stopping token is already cancelled on a graceful shutdown, so the flush gets its own timeout.
            using var timeout = new CancellationTokenSource(FinalFlushTimeout, timeProvider);
            await tracker.FlushAsync(timeout.Token);
        } catch (Exception exception) {
            LogFinalFlushFailed(logger, exception, subscriptionId, tracker.LastPosition);
        }
    }

    [LoggerMessage(Message = "Subscription '{SubscriptionId}' subscribing after position {Position} (start from end: {StartFromEnd})")]
    static partial void LogSubscribing(ILogger logger, LogLevel level, string subscriptionId, ulong? position, bool startFromEnd);

    [LoggerMessage(Message = "Subscription '{SubscriptionId}' failed at position {Position}; subscribing again in {Delay} (attempt {Attempt})")]
    static partial void LogResubscribing(ILogger logger, LogLevel level, Exception exception, string subscriptionId, ulong? position, int attempt, TimeSpan delay);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Subscription '{SubscriptionId}' ended unexpectedly at position {Position}; subscribing again in {Delay} (attempt {Attempt})")]
    static partial void LogSubscriptionEnded(ILogger logger, string subscriptionId, ulong? position, int attempt, TimeSpan delay);

    [LoggerMessage(Level = LogLevel.Information, Message = "Subscription '{SubscriptionId}' receives messages again after position {Position} (attempt {Attempt})")]
    static partial void LogResubscribed(ILogger logger, string subscriptionId, ulong? position, int attempt);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Subscription '{SubscriptionId}' could not deserialize the message at position {Position}; parking it")]
    static partial void LogParkingUndeserializable(ILogger logger, Exception exception, string subscriptionId, ulong position);

    [LoggerMessage(Level = LogLevel.Critical, Message = "Subscription '{SubscriptionId}' stopped at position {Position} after a failure that is not transient")]
    static partial void LogSubscriptionFailed(ILogger logger, Exception exception, string subscriptionId, ulong? position);

    [LoggerMessage(Level = LogLevel.Error, Message = "Subscription '{SubscriptionId}' could not write its final checkpoint {Position}")]
    static partial void LogFinalFlushFailed(ILogger logger, Exception exception, string subscriptionId, ulong? position);
}
