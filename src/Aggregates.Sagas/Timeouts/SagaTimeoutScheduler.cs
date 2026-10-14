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
sealed class SagaTimeoutScheduler(
    ISagaTimeoutStore store,
    ICheckpointStore checkpointStore,
    ISubscriptionFactory subscriptionFactory,
    SubscriptionCheckpointOptions checkpointOptions,
    SubscriptionResubscribeOptions resubscribeOptions,
    TimeProvider timeProvider,
    ILogger<SagaTimeoutScheduler> logger
) {

    /// <summary>
    /// The longest single wait: timers can't wait months (spike v1-19 T3), and a clock that jumps
    /// is noticed within it.
    /// </summary>
    internal static readonly TimeSpan MaxWait = TimeSpan.FromMinutes(1);

    static readonly TimeSpan FinalFlushTimeout = TimeSpan.FromSeconds(5);

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
        var retry = new SagaTimeoutRetry(subscriptionFactory, resubscribeOptions, timeProvider, logger, subscriptionId);
        try {
            if (!await retry.ExecuteAsync(ct => activation.IsActivatedAsync(registration, ct), "check whether the saga requested timeouts", stoppingToken)) {
                logger.LogWaiting(subscriptionId);
                await activation.Activated.WaitAsync(stoppingToken);
                logger.LogActivated(subscriptionId);
            }
            await RunActiveAsync(registration, subscriptionId, retry, deliver, stoppingToken);
        } catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested) {
        } catch (Exception exception) {
            logger.LogStopped(exception, subscriptionId);
            throw;
        }
    }

    /// <summary>
    /// Reads the checkpoint, starts the feed, and runs a <see cref="SagaTimeoutRun"/> until the host
    /// stops or a failure ends it; the checkpoint is flushed in every case.
    /// </summary>
    async Task RunActiveAsync(SubscriptionRegistration registration, string subscriptionId, SagaTimeoutRetry retry,
        Func<OpenSagaTimeout, CancellationToken, ValueTask> deliver, CancellationToken stoppingToken) {
        var start = await retry.ExecuteAsync(ct => GetStartPositionAsync(registration, subscriptionId, ct), "read its checkpoint", stoppingToken);
        logger.LogReading(subscriptionId, start);

        var owners = new HashSet<string>([registration.SubscriptionId, .. registration.Predecessors ?? []], StringComparer.Ordinal);
        var index = new SagaTimeoutIndex(owners, start, store.DeserializeCommand);
        var tracker = new CheckpointTracker(checkpointStore, subscriptionId, checkpointOptions, timeProvider);
        try {
            await using var feed = SagaTimeoutFeed.Start(store, retry, timeProvider, logger, subscriptionId, start, stoppingToken);
            await new SagaTimeoutRun(store, index, tracker, retry, deliver, timeProvider, logger, subscriptionId, start).RunAsync(feed, stoppingToken);
        } finally {
            await FlushFinalCheckpointAsync(tracker, subscriptionId);
        }
    }

    /// <summary>
    /// The scheduler's own checkpoint, or the nearest predecessor's, which is adopted. Null for the
    /// beginning.
    /// </summary>
    async ValueTask<ulong?> GetStartPositionAsync(SubscriptionRegistration registration, string subscriptionId, CancellationToken cancellationToken) {
        string[] predecessors = [.. (registration.Predecessors ?? []).Select(SagaTimeouts.SubscriptionId)];
        if (await checkpointStore.GetOrAdoptAsync(subscriptionId, predecessors, cancellationToken) is not { } checkpoint)
            return null;
        if (checkpoint.AdoptedFrom == 0)
            logger.LogContinuingFromPredecessor(subscriptionId, predecessors[0], checkpoint.Position);
        else if (checkpoint.AdoptedFrom is { } i)
            logger.LogContinuingFromOlderPredecessor(subscriptionId, predecessors[i], checkpoint.Position, string.Join(", ", predecessors.Take(i)));
        return checkpoint.Position;
    }

    /// <summary>
    /// Flushes the tracker's pending checkpoint, with its own timeout because stopping is already
    /// cancelled.
    /// </summary>
    async ValueTask FlushFinalCheckpointAsync(CheckpointTracker tracker, string subscriptionId) {
        try {
            using var timeout = new CancellationTokenSource(FinalFlushTimeout, timeProvider);
            await tracker.FlushAsync(timeout.Token);
        } catch (Exception exception) {
            logger.LogFinalFlushFailed(exception, subscriptionId, tracker.LastPosition);
        }
    }
}
