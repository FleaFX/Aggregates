using System.Globalization;
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
///   <item>When the subscription has no checkpoint of its own, the loop continues after the
///   checkpoint of the nearest predecessor that has one (see
///   <see cref="SubscriptionRegistration.Predecessors"/>), adopts it by writing it as the
///   subscription's own first checkpoint before subscribing, and starts after it. A predecessor's
///   checkpoint wins over starting from the end. Without a predecessor checkpoint, the
///   subscription starts from the beginning, or from the end when the registration says so,
///   fixed at the end position the transport reports before the first subscribe, so a
///   subscription that stops before its first event misses none appended meanwhile. With
///   <see cref="SubscriptionCheckpointOptions.RequireCheckpoint"/>, it fails instead, before it
///   subscribes, unless its id is in <see cref="SubscriptionCheckpointOptions.AllowedWithoutCheckpoint"/>;
///   that failure is never transient.</item>
///   <item>When an <see cref="EventTypeRegistry"/> is registered, the loop asks it for the
///   stored names of the event types the registration can receive (see
///   <see cref="EventTypeRegistry.GetTypeNames"/>) and the transport delivers only those.
///   Without a registry, for a registration on <see langword="object"/>, or when the registry
///   knows no matching event type (logged as a warning), the subscription receives every event
///   type. The names are computed once per run and reused by every resubscribe.</item>
///   <item>A progress message (see <see cref="SubscriptionMessage.IsProgress"/>) reports how far
///   the store read events the subscription's filter excluded; it is not processed, but written
///   as the checkpoint right away. The store reports one per
///   <see cref="SubscriptionCheckpointOptions.MaxFilteredEvents"/> events it read, so a quiet
///   subscription still advances.</item>
///   <item>Whether a failure is transient is decided by <see cref="ISubscriptionFactory.IsTransient"/>.
///   Transient failures, and a subscription that ends without failure, lead to a new subscription
///   after a delay as configured by <see cref="SubscriptionResubscribeOptions"/>. Any other
///   failure is logged and rethrown.</item>
///   <item>A message whose <see cref="SubscriptionMessage.DeserializationError"/> is set is parked
///   right away, without retries, and not handed to the processing delegate. When the registration
///   blocks instead (<see cref="SubscriptionErrorMode.Block"/>), the loop stops before the message
///   and subscribes again after the resubscribe delay, so the store delivers and deserializes it
///   once more; it continues once the message deserializes, typically after a fix is deployed.</item>
///   <item>When the loop ends, the last processed position is written once more. A failure to
///   write it is logged and never replaces the exception that ended the loop.</item>
///   <item>When a <see cref="SubscriptionMonitor"/> is given, the loop reports its state there:
///   running when it starts and when a message arrives after a failure, retrying after a transient
///   failure or an unexpected end, blocked before a message that can't be deserialized when the
///   registration blocks, and stopped when it ends, with the failure that ended it.</item>
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
    ILogger<SubscriptionLoop> logger,
    EventTypeRegistry? eventTypeRegistry = null,
    SubscriptionMonitor? monitor = null
) {

    static readonly TimeSpan FinalFlushTimeout = TimeSpan.FromSeconds(5);

    // From this many consecutive failures on, a resubscribe is logged as an error instead of a warning.
    const int ErrorLevelFailures = 5;

    /// <summary>
    /// Runs the subscription until <paramref name="stoppingToken"/> is cancelled or a failure
    /// that is not transient occurs.
    /// </summary>
    /// <param name="registration">
    /// The subscription to run: its id, whether it starts from the end of the store when it has
    /// no checkpoint yet, and the predecessors whose checkpoint it continues from.
    /// </param>
    /// <param name="process">
    /// Handles one deserialized message. Exceptions it throws are treated as subscription failures;
    /// handler failures should be retried and parked within it, with <see cref="SubscriptionRetryPolicy"/>.
    /// </param>
    /// <param name="stoppingToken">Stops the loop.</param>
    public async Task RunAsync(
        SubscriptionRegistration registration,
        Func<SubscriptionMessage, CancellationToken, ValueTask> process,
        CancellationToken stoppingToken) {

        var subscriptionId = registration.SubscriptionId;
        var tracker = new CheckpointTracker(checkpointStore, subscriptionId, checkpointOptions, timeProvider);
        var eventTypes = GetEventTypes(registration);
        var failures = 0;
        ulong? from = null;
        Exception? failure = null;
        monitor?.Report(subscriptionId, SubscriptionState.Running);
        try {
            while (true) {
                from = tracker.LastPosition;
                TimeSpan delay;
                try {
                    from ??= await GetStartPositionAsync(registration, stoppingToken);
                    LogSubscribing(logger, failures == 0 ? LogLevel.Information : LogLevel.Debug, subscriptionId, from,
                        registration.StartFromEnd, eventTypes?.Count.ToString(CultureInfo.InvariantCulture) ?? "all");

                    await using var subscription = subscriptionFactory.Subscribe(
                        new SubscriptionRequest(from, registration.StartFromEnd, eventTypes, checkpointOptions.MaxFilteredEvents), stoppingToken);
                    SubscriptionMessage? blocked = null;
                    await foreach (var message in subscription.WithCancellation(stoppingToken)) {
                        if (message.IsProgress) {
                            // Progress at or before the known position (KurrentDB reports 0 when it starts) is no progress.
                            if (message.CommitPosition > (tracker.LastPosition ?? from ?? 0))
                                await tracker.RecordProgressAsync(message.CommitPosition, stoppingToken);
                        } else {
                            if (message.DeserializationError is not null && registration.ErrorMode == SubscriptionErrorMode.Block) {
                                // Not parked and not processed: the next subscription delivers it again.
                                blocked = message;
                                break;
                            }

                            if (message.DeserializationError is { } error) {
                                LogParkingUndeserializable(logger, error, subscriptionId, message.CommitPosition);
                                await parkedMessageSink.ParkAsync(subscriptionId, message, error, stoppingToken);
                            } else {
                                await process(message, stoppingToken);
                            }

                            await tracker.AdvanceAsync(message.CommitPosition, stoppingToken);
                        }

                        if (failures > 0) {
                            LogResubscribed(logger, subscriptionId, from, failures);
                            monitor?.Report(subscriptionId, SubscriptionState.Running, message.CommitPosition);
                            failures = 0;
                        }
                    }

                    delay = NextDelay(++failures);
                    if (blocked is { } undeserializable) {
                        LogBlockedUndeserializable(logger, undeserializable.DeserializationError!, subscriptionId, undeserializable.CommitPosition, failures, delay);
                        monitor?.Report(subscriptionId, SubscriptionState.Blocked, undeserializable.CommitPosition, failures, undeserializable.DeserializationError);
                    } else {
                        LogSubscriptionEnded(logger, subscriptionId, tracker.LastPosition ?? from, failures, delay);
                        monitor?.Report(subscriptionId, SubscriptionState.Retrying, tracker.LastPosition ?? from, failures);
                    }
                } catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested) {
                    return;
                } catch (Exception exception) when (exception is not MissingCheckpointException && subscriptionFactory.IsTransient(exception)) {
                    delay = NextDelay(++failures);
                    LogResubscribing(logger, failures >= ErrorLevelFailures ? LogLevel.Error : LogLevel.Warning,
                        exception, subscriptionId, tracker.LastPosition ?? from, failures, delay);
                    monitor?.Report(subscriptionId, SubscriptionState.Retrying, tracker.LastPosition ?? from, failures, exception);
                } catch (Exception exception) {
                    failure = exception;
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
            monitor?.Report(subscriptionId, SubscriptionState.Stopped, tracker.LastPosition ?? from, error: failure);
            await FlushFinalCheckpointAsync(tracker, subscriptionId);
        }
    }

    // The start position: the subscription's own checkpoint, or the nearest predecessor's,
    // which is adopted before subscribing. Null for a fresh start (or end) without adoption,
    // which RequireCheckpoint refuses unless the id is allowed.
    async ValueTask<ulong?> GetStartPositionAsync(SubscriptionRegistration registration, CancellationToken stoppingToken) {
        var predecessors = registration.Predecessors ?? [];
        if (await checkpointStore.GetOrAdoptAsync(registration.SubscriptionId, predecessors, stoppingToken) is { } checkpoint) {
            if (checkpoint.AdoptedFrom == 0)
                LogContinuingFromPredecessor(logger, registration.SubscriptionId, predecessors[0], checkpoint.Position);
            else if (checkpoint.AdoptedFrom is { } i)
                LogContinuingFromOlderPredecessor(logger, registration.SubscriptionId, predecessors[i], checkpoint.Position,
                    string.Join(", ", predecessors.Take(i)));
            return checkpoint.Position;
        }

        var start = registration.StartFromEnd ? "end" : "beginning";
        if (checkpointOptions.RequireCheckpoint) {
            if (!checkpointOptions.AllowedWithoutCheckpoint.Contains(registration.SubscriptionId))
                throw new MissingCheckpointException(registration.SubscriptionId, predecessors, start);
            LogAllowedWithoutCheckpoint(logger, registration.SubscriptionId, start);
        }

        if (predecessors.Count > 0)
            LogNoPredecessorCheckpoint(logger, registration.SubscriptionId, string.Join(", ", predecessors), start);

        if (registration.StartFromEnd && await subscriptionFactory.GetEndPositionAsync(stoppingToken) is { } end) {
            await checkpointStore.StoreAsync(registration.SubscriptionId, end, stoppingToken);
            LogStartingFromEnd(logger, registration.SubscriptionId, end);
            return end;
        }
        return null;
    }

    // The stored event types the subscription can receive, or null to receive every event type.
    IReadOnlyList<string>? GetEventTypes(SubscriptionRegistration registration) {
        if (eventTypeRegistry is null || registration.EventType == typeof(object))
            return null;

        var names = eventTypeRegistry.GetTypeNames(registration.EventType);
        if (names.Count == 0) {
            LogNoKnownEventTypes(logger, registration.SubscriptionId, registration.EventType.FullName);
            return null;
        }

        LogEventTypes(logger, registration.SubscriptionId, string.Join(", ", names));
        return names;
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

    /// <summary>
    /// Thrown when a subscription would start without a checkpoint while
    /// <see cref="SubscriptionCheckpointOptions.RequireCheckpoint"/> is on. Never transient.
    /// </summary>
    /// <param name="subscriptionId">The subscription.</param>
    /// <param name="predecessors">Its predecessors, none of which has a checkpoint.</param>
    /// <param name="start">Where it would have started: "beginning" or "end".</param>
    sealed class MissingCheckpointException(string subscriptionId, IReadOnlyList<string> predecessors, string start)
        : InvalidOperationException(
            $"Subscription '{subscriptionId}' has no checkpoint, and " +
            (predecessors.Count == 0 ? "it has no predecessor" : $"neither have its predecessors {string.Join(", ", predecessors)}") +
            $"; it would start from the {start}, but SubscriptionCheckpointOptions.RequireCheckpoint is on. " +
            "If its position exists elsewhere, write it first: import the 0.x positions (Aggregates.KurrentDB.Migration), " +
            "restore the checkpoint store, or set continueFrom to the subscription it continues from. " +
            $"To start it from the {start} on purpose, add '{subscriptionId}' to SubscriptionCheckpointOptions.AllowedWithoutCheckpoint.");

    [LoggerMessage(Level = LogLevel.Information, Message = "Subscription '{SubscriptionId}' has no checkpoint and is allowed to start without one; starting from the {Start}")]
    static partial void LogAllowedWithoutCheckpoint(ILogger logger, string subscriptionId, string start);

    [LoggerMessage(Level = LogLevel.Information, Message = "Subscription '{SubscriptionId}' has no checkpoint; continuing from '{Predecessor}' after position {Position}")]
    static partial void LogContinuingFromPredecessor(ILogger logger, string subscriptionId, string predecessor, ulong position);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Subscription '{SubscriptionId}' has no checkpoint; continuing from '{Predecessor}' after position {Position}, because {Skipped} has no checkpoint. If a skipped version replayed on purpose, that replay did not happen here.")]
    static partial void LogContinuingFromOlderPredecessor(ILogger logger, string subscriptionId, string predecessor, ulong position, string skipped);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Subscription '{SubscriptionId}' has no checkpoint and neither have its predecessors {Predecessors}; starting from the {Start}")]
    static partial void LogNoPredecessorCheckpoint(ILogger logger, string subscriptionId, string predecessors, string start);

    [LoggerMessage(Message = "Subscription '{SubscriptionId}' subscribing after position {Position} (start from end: {StartFromEnd}, event types: {EventTypes})")]
    static partial void LogSubscribing(ILogger logger, LogLevel level, string subscriptionId, ulong? position, bool startFromEnd, string eventTypes);

    [LoggerMessage(Level = LogLevel.Debug, Message = "Subscription '{SubscriptionId}' receives the event types {EventTypes}")]
    static partial void LogEventTypes(ILogger logger, string subscriptionId, string eventTypes);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Subscription '{SubscriptionId}' handles {EventType}, but the EventTypeRegistry knows no event type it can receive; it receives every event type instead. Add the assembly with these events to the registry.")]
    static partial void LogNoKnownEventTypes(ILogger logger, string subscriptionId, string? eventType);

    [LoggerMessage(Level = LogLevel.Information, Message = "Subscription '{SubscriptionId}' has no checkpoint; starting from the end of the store at position {Position}")]
    static partial void LogStartingFromEnd(ILogger logger, string subscriptionId, ulong position);

    [LoggerMessage(Message = "Subscription '{SubscriptionId}' failed at position {Position}; subscribing again in {Delay} (attempt {Attempt})")]
    static partial void LogResubscribing(ILogger logger, LogLevel level, Exception exception, string subscriptionId, ulong? position, int attempt, TimeSpan delay);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Subscription '{SubscriptionId}' ended unexpectedly at position {Position}; subscribing again in {Delay} (attempt {Attempt})")]
    static partial void LogSubscriptionEnded(ILogger logger, string subscriptionId, ulong? position, int attempt, TimeSpan delay);

    [LoggerMessage(Level = LogLevel.Information, Message = "Subscription '{SubscriptionId}' receives messages again after position {Position} (attempt {Attempt})")]
    static partial void LogResubscribed(ILogger logger, string subscriptionId, ulong? position, int attempt);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Subscription '{SubscriptionId}' could not deserialize the message at position {Position}; parking it")]
    static partial void LogParkingUndeserializable(ILogger logger, Exception exception, string subscriptionId, ulong position);

    [LoggerMessage(Level = LogLevel.Error, Message = "Subscription '{SubscriptionId}' could not deserialize the message at position {Position} and blocks instead of parking it; subscribing again in {Delay} (attempt {Attempt})")]
    static partial void LogBlockedUndeserializable(ILogger logger, Exception exception, string subscriptionId, ulong position, int attempt, TimeSpan delay);

    [LoggerMessage(Level = LogLevel.Critical, Message = "Subscription '{SubscriptionId}' stopped at position {Position} after a failure that is not transient")]
    static partial void LogSubscriptionFailed(ILogger logger, Exception exception, string subscriptionId, ulong? position);

    [LoggerMessage(Level = LogLevel.Error, Message = "Subscription '{SubscriptionId}' could not write its final checkpoint {Position}")]
    static partial void LogFinalFlushFailed(ILogger logger, Exception exception, string subscriptionId, ulong? position);
}
