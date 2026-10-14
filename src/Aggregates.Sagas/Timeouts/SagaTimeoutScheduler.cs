using System.Text;
using System.Threading.Channels;
using Aggregates.Subscriptions;
using Microsoft.Extensions.Logging;

namespace Aggregates.Sagas;

/// <summary>
/// Executes the timeouts of a saga at their due time. It sleeps until the saga is activated
/// (see <see cref="SagaTimeoutActivation"/>), then reads the timeout entries of every saga stream
/// from the store, keeps the open timeouts of its saga in memory, and hands each one to a delivery
/// delegate once it is due — starting only after it has read back a marker it appended, so that it
/// knows every timeout stored and handled before it started. After each delivery it records the
/// timeout as handled in its own stream (<c>timeouts-{id}</c>).
/// </summary>
/// <remarks>
/// Its checkpoint (<c>checkpoint-timeouts-{id}</c>) stays before the oldest open request, so a
/// restart reads every open timeout again. It waits at most <see cref="MaxWait"/> at a time and
/// reads the clock from <see cref="TimeProvider"/>. Transient failures (see
/// <see cref="ISubscriptionFactory.IsTransient"/>) are retried with backoff; any other failure
/// ends <see cref="RunAsync"/>. One instance serves every saga.
/// </remarks>
sealed partial class SagaTimeoutScheduler(
    ISagaTimeoutStore store,
    ICheckpointStore checkpointStore,
    ISubscriptionFactory subscriptionFactory,
    SubscriptionCheckpointOptions checkpointOptions,
    SubscriptionResubscribeOptions resubscribeOptions,
    TimeProvider timeProvider,
    ILogger<SagaTimeoutScheduler> logger) {

    /// <summary>
    /// The longest single wait: timers can't wait months (spike v1-19 T3), and a clock that jumps
    /// is noticed within it.
    /// </summary>
    internal static readonly TimeSpan MaxWait = TimeSpan.FromMinutes(1);

    static readonly TimeSpan FinalFlushTimeout = TimeSpan.FromSeconds(5);

    // From this many consecutive failures on, a resubscribe is logged as an error instead of a warning.
    const int ErrorLevelFailures = 5;

    /// <summary>
    /// Runs the scheduler of the saga of <paramref name="registration"/> until
    /// <paramref name="stoppingToken"/> is cancelled or a failure that is not transient occurs.
    /// </summary>
    /// <param name="registration">The subscription of the saga.</param>
    /// <param name="activation">Tells whether the saga ever requested a timeout.</param>
    /// <param name="deliver">Executes one due timeout; it retries and parks failures itself.</param>
    /// <param name="stoppingToken">Stops the scheduler.</param>
    public async Task RunAsync(SubscriptionRegistration registration, SagaTimeoutActivation activation,
        Func<OpenSagaTimeout, CancellationToken, ValueTask> deliver, CancellationToken stoppingToken) {
        var subscriptionId = SagaTimeouts.SubscriptionId(registration.SubscriptionId);
        try {
            if (!await RetryAsync(ct => activation.IsActivatedAsync(registration, ct), subscriptionId, "check whether the saga requested timeouts", stoppingToken)) {
                LogWaiting(logger, subscriptionId);
                await activation.Activated.WaitAsync(stoppingToken);
                LogActivated(logger, subscriptionId);
            }
            await RunActiveAsync(registration, subscriptionId, deliver, stoppingToken);
        } catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested) {
        } catch (Exception exception) {
            LogStopped(logger, exception, subscriptionId);
            throw;
        }
    }

    /// <summary>
    /// Runs the index, the feed and the deliveries until the host stops or a failure ends them.
    /// </summary>
    async Task RunActiveAsync(SubscriptionRegistration registration, string subscriptionId,
        Func<OpenSagaTimeout, CancellationToken, ValueTask> deliver, CancellationToken stoppingToken) {
        var start = await RetryAsync(ct => GetStartPositionAsync(registration, subscriptionId, ct), subscriptionId, "read its checkpoint", stoppingToken);
        LogReading(logger, subscriptionId, start);

        var owners = new HashSet<string>([registration.SubscriptionId, .. registration.Predecessors ?? []], StringComparer.Ordinal);
        var index = new SagaTimeoutIndex(owners, start, store.DeserializeCommand);
        var tracker = new CheckpointTracker(checkpointStore, subscriptionId, checkpointOptions, timeProvider);
        var channel = Channel.CreateUnbounded<SagaTimeoutEntry>(new UnboundedChannelOptions { SingleReader = true, SingleWriter = true });
        using var stopReading = CancellationTokenSource.CreateLinkedTokenSource(stoppingToken);
        var reader = ReadAsync(subscriptionId, start, channel.Writer, stopReading.Token);

        // Appended after the feed started: once the feed delivers it, the index holds every timeout stored before.
        var marker = Guid.NewGuid().ToString("N");
        await RetryAsync(async ct => {
            await store.AppendAsync(subscriptionId, SagaTimeoutFormat.MarkerEventType, Encoding.UTF8.GetBytes(marker), ct);
            return true;
        }, subscriptionId, "append its marker", stoppingToken);

        var caughtUp = false;
        var stored = start;
        var deliveryFailures = 0;
        try {
            while (true) {
                while (channel.Reader.TryRead(out var entry)) {
                    switch (index.Apply(entry, out var requested)) {
                        case SagaTimeoutChange.Requested when requested.Error is { } error:
                            LogUndeserializable(logger, error, subscriptionId, requested.Request, requested.Request.StreamId, requested.Due);
                            break;
                        case SagaTimeoutChange.Requested:
                            LogRequested(logger, subscriptionId, requested.Request, requested.Request.StreamId, requested.Due, requested.Command!.GetType().Name);
                            break;
                        case SagaTimeoutChange.Invalid:
                            LogInvalidRequest(logger, subscriptionId, entry.Source);
                            break;
                        case SagaTimeoutChange.Marker when !caughtUp && entry.Source.StreamId == subscriptionId
                            && Encoding.UTF8.GetString(entry.Data.Span) == marker:
                            caughtUp = true;
                            LogCaughtUp(logger, subscriptionId, index.Count, index.NextDue);
                            break;
                    }
                }

                if (index.Checkpoint is { } checkpoint && (stored is null || checkpoint > stored)) {
                    await tracker.AdvanceAsync(checkpoint, stoppingToken);
                    stored = checkpoint;
                }

                var now = timeProvider.GetUtcNow();
                if (caughtUp && index.TryPeekDue(now, out var due)) {
                    try {
                        await deliver(due, stoppingToken);
                        deliveryFailures = 0;
                    } catch (Exception exception) when (exception is not OperationCanceledException && subscriptionFactory.IsTransient(exception)) {
                        var delay = NextDelay(++deliveryFailures);
                        LogDeliveryFailed(logger, exception, subscriptionId, due.Request, delay, deliveryFailures);
                        await Task.Delay(delay, timeProvider, stoppingToken);
                        continue;
                    }
                    // Recorded after the command, so a crash in between runs the command again (at least once).
                    await RetryAsync(async ct => {
                        await store.AppendAsync(subscriptionId, SagaTimeoutFormat.DeliveryEventType, due.Request.ToLinkData(), ct);
                        return true;
                    }, subscriptionId, "record a delivery", stoppingToken);
                    index.Remove(due.Request);
                    continue;
                }

                var wait = caughtUp && index.NextDue is { } next && next - now < MaxWait ? next - now : MaxWait;
                if (wait <= TimeSpan.Zero)
                    continue;
                using var timer = new CancellationTokenSource(wait, timeProvider);
                using var wake = CancellationTokenSource.CreateLinkedTokenSource(stoppingToken, timer.Token);
                try {
                    if (!await channel.Reader.WaitToReadAsync(wake.Token)) {
                        // The feed ended: rethrow the failure that ended it.
                        await reader;
                        return;
                    }
                } catch (OperationCanceledException) when (!stoppingToken.IsCancellationRequested) {
                    // The wait ended: a timeout may be due now.
                }
            }
        } finally {
            await stopReading.CancelAsync();
            try {
                await reader;
            } catch (Exception) {
                // Already rethrown above, or cancelled.
            }
            await FlushFinalCheckpointAsync(tracker, subscriptionId);
        }
    }

    /// <summary>
    /// The scheduler's own checkpoint, or the nearest predecessor's, which is adopted. Null for the
    /// beginning.
    /// </summary>
    async ValueTask<ulong?> GetStartPositionAsync(SubscriptionRegistration registration, string subscriptionId, CancellationToken cancellationToken) {
        if (await checkpointStore.GetAsync(subscriptionId, cancellationToken) is { } own)
            return own;
        foreach (var predecessor in registration.Predecessors ?? []) {
            var predecessorId = SagaTimeouts.SubscriptionId(predecessor);
            if (await checkpointStore.GetAsync(predecessorId, cancellationToken) is not { } inherited)
                continue;
            await checkpointStore.StoreAsync(subscriptionId, inherited, cancellationToken);
            LogContinuingFromPredecessor(logger, subscriptionId, predecessorId, inherited);
            return inherited;
        }
        return null;
    }

    /// <summary>
    /// Reads the store into the channel, subscribing again after a transient failure or an unexpected end.
    /// </summary>
    async Task ReadAsync(string subscriptionId, ulong? start, ChannelWriter<SagaTimeoutEntry> writer, CancellationToken cancellationToken) {
        var from = start;
        var failures = 0;
        try {
            while (true) {
                TimeSpan delay;
                try {
                    await foreach (var entry in store.SubscribeAsync(from, cancellationToken)) {
                        await writer.WriteAsync(entry, cancellationToken);
                        if (!entry.IsProgress || entry.CommitPosition > (from ?? 0))
                            from = entry.CommitPosition;
                        failures = 0;
                    }
                    delay = NextDelay(++failures);
                    LogFeedEnded(logger, subscriptionId, from, delay, failures);
                } catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) {
                    return;
                } catch (Exception exception) when (subscriptionFactory.IsTransient(exception)) {
                    delay = NextDelay(++failures);
                    LogFeedFailed(logger, failures >= ErrorLevelFailures ? LogLevel.Error : LogLevel.Warning, exception, subscriptionId, from, delay, failures);
                }

                try {
                    await Task.Delay(delay, timeProvider, cancellationToken);
                } catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) {
                    return;
                }
            }
        } finally {
            writer.TryComplete();
        }
    }

    /// <summary>
    /// Runs <paramref name="action"/> until it succeeds, with backoff while it fails transiently.
    /// </summary>
    async ValueTask<T> RetryAsync<T>(Func<CancellationToken, ValueTask<T>> action, string subscriptionId, string what, CancellationToken cancellationToken) {
        for (var attempt = 1; ; attempt++) {
            try {
                return await action(cancellationToken);
            } catch (Exception exception) when (exception is not OperationCanceledException && subscriptionFactory.IsTransient(exception)) {
                var delay = NextDelay(attempt);
                LogRetrying(logger, exception, subscriptionId, what, delay, attempt);
                await Task.Delay(delay, timeProvider, cancellationToken);
            }
        }
    }

    /// <summary>
    /// The backoff delay before the next attempt.
    /// </summary>
    TimeSpan NextDelay(int failures) =>
        Backoff.Delay(failures, resubscribeOptions.InitialDelay, resubscribeOptions.MaxDelay, resubscribeOptions.BackoffMultiplier);

    /// <summary>
    /// Flushes the tracker's pending checkpoint, with its own timeout because stopping is already
    /// cancelled.
    /// </summary>
    async ValueTask FlushFinalCheckpointAsync(CheckpointTracker tracker, string subscriptionId) {
        try {
            // The stopping token is already cancelled on a graceful shutdown, so the flush gets its own timeout.
            using var timeout = new CancellationTokenSource(FinalFlushTimeout, timeProvider);
            await tracker.FlushAsync(timeout.Token);
        } catch (Exception exception) {
            LogFinalFlushFailed(logger, exception, subscriptionId, tracker.LastPosition);
        }
    }

    [LoggerMessage(Level = LogLevel.Debug, Message = "Timeout scheduler '{SubscriptionId}' waits until the saga requests its first timeout")]
    static partial void LogWaiting(ILogger logger, string subscriptionId);

    [LoggerMessage(Level = LogLevel.Information, Message = "Timeout scheduler '{SubscriptionId}' starts: the saga requested its first timeout")]
    static partial void LogActivated(ILogger logger, string subscriptionId);

    [LoggerMessage(Level = LogLevel.Information, Message = "Timeout scheduler '{SubscriptionId}' has no checkpoint; continuing from '{Predecessor}' after position {Position}")]
    static partial void LogContinuingFromPredecessor(ILogger logger, string subscriptionId, string predecessor, ulong position);

    [LoggerMessage(Level = LogLevel.Information, Message = "Timeout scheduler '{SubscriptionId}' reading after position {Position}")]
    static partial void LogReading(ILogger logger, string subscriptionId, ulong? position);

    [LoggerMessage(Level = LogLevel.Information, Message = "Timeout scheduler '{SubscriptionId}' caught up with {Count} open timeout(s); the next one is due at {Due}")]
    static partial void LogCaughtUp(ILogger logger, string subscriptionId, int count, DateTimeOffset? due);

    [LoggerMessage(Level = LogLevel.Debug, Message = "Timeout scheduler '{SubscriptionId}' read timeout {Request} for saga {SagaId}, due at {Due}: {CommandType}")]
    static partial void LogRequested(ILogger logger, string subscriptionId, EventReference request, string sagaId, DateTimeOffset due, string commandType);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Timeout scheduler '{SubscriptionId}' can't deserialize the command of timeout {Request} for saga {SagaId}, due at {Due}; it is parked when it is due, unless a deploy fixes it before")]
    static partial void LogUndeserializable(ILogger logger, Exception exception, string subscriptionId, EventReference request, string sagaId, DateTimeOffset due);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Timeout scheduler '{SubscriptionId}' skips the invalid timeout request {Request}")]
    static partial void LogInvalidRequest(ILogger logger, string subscriptionId, EventReference request);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Timeout scheduler '{SubscriptionId}' could not {Action}; trying again in {Delay} (attempt {Attempt})")]
    static partial void LogRetrying(ILogger logger, Exception exception, string subscriptionId, string action, TimeSpan delay, int attempt);

    [LoggerMessage(Message = "Timeout scheduler '{SubscriptionId}' failed at position {Position}; subscribing again in {Delay} (attempt {Attempt})")]
    static partial void LogFeedFailed(ILogger logger, LogLevel level, Exception exception, string subscriptionId, ulong? position, TimeSpan delay, int attempt);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Timeout scheduler '{SubscriptionId}' ended unexpectedly at position {Position}; subscribing again in {Delay} (attempt {Attempt})")]
    static partial void LogFeedEnded(ILogger logger, string subscriptionId, ulong? position, TimeSpan delay, int attempt);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Timeout scheduler '{SubscriptionId}' could not deliver timeout {Request}; trying again in {Delay} (attempt {Attempt})")]
    static partial void LogDeliveryFailed(ILogger logger, Exception exception, string subscriptionId, EventReference request, TimeSpan delay, int attempt);

    [LoggerMessage(Level = LogLevel.Critical, Message = "Timeout scheduler '{SubscriptionId}' stopped after a failure that is not transient")]
    static partial void LogStopped(ILogger logger, Exception exception, string subscriptionId);

    [LoggerMessage(Level = LogLevel.Error, Message = "Timeout scheduler '{SubscriptionId}' could not write its final checkpoint {Position}")]
    static partial void LogFinalFlushFailed(ILogger logger, Exception exception, string subscriptionId, ulong? position);
}
