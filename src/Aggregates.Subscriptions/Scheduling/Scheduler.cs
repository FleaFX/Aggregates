using System.ComponentModel;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace Aggregates.Subscriptions.Internal;

/// <summary>
/// Executes the commands a saga or policy deferred, and delivers the deadlines a saga requested, at
/// their due time. It sleeps until the handler is activated (see <see cref="SchedulerActivation"/>),
/// then reads the scheduled entries of every stream from the store, keeps the open requests of its
/// handler in memory, and executes or delivers each one once it is due — starting only after it has
/// read back a marker it appended, so that it knows every request stored and handled before it
/// started. After each delivery it records the request as handled in its own stream
/// (<c>scheduled-{id}</c>).
/// </summary>
/// <remarks>
/// <para>
/// Internal infrastructure of the Aggregates packages. Public only so that those packages can use it;
/// it is not covered by semantic versioning and may change in any release.
/// </para>
/// Its checkpoint (<c>checkpoint-scheduled-{id}</c>) stays before the oldest open request, so a
/// restart reads every open request again. It waits at most one minute at a time and reads the
/// clock from <see cref="TimeProvider"/>. A command that keeps failing is parked under
/// <c>scheduled-{id}</c>. Transient failures of the store (see
/// <see cref="ISubscriptionFactory.IsTransient"/>) are retried with backoff; any other failure
/// ends <see cref="RunAsync(SubscriptionRegistration, SchedulerActivation, CancellationToken)"/>.
/// When a <see cref="SubscriptionMonitor"/> is given, it reports its state there under its id:
/// waiting until its handler is activated, running, retrying after a transient failure until the
/// failed action succeeds or its feed reads again, and stopped when it ends.
/// One instance serves every saga and policy.
/// </remarks>
/// <param name="store">The store of the scheduled entries.</param>
/// <param name="checkpointStore">The store of the scheduler's checkpoint.</param>
/// <param name="subscriptionFactory">Decides which failures are transient.</param>
/// <param name="checkpointOptions">How often the checkpoint is written.</param>
/// <param name="resubscribeOptions">The backoff after a transient failure.</param>
/// <param name="scopeFactory">Creates the scope a deferred command is dispatched in.</param>
/// <param name="retryPolicy">Retries and parks a failing command.</param>
/// <param name="parkedMessageSink">Parks a command that can't be deserialized.</param>
/// <param name="timeProvider">The clock.</param>
/// <param name="loggerFactory">Creates the loggers of the scheduler and of the command dispatch.</param>
/// <param name="monitor">Receives the state of the scheduler; optional.</param>
[EditorBrowsable(EditorBrowsableState.Never)]
public sealed class Scheduler(
    IScheduledStore store,
    ICheckpointStore checkpointStore,
    ISubscriptionFactory subscriptionFactory,
    SubscriptionCheckpointOptions checkpointOptions,
    SubscriptionResubscribeOptions resubscribeOptions,
    IServiceScopeFactory scopeFactory,
    SubscriptionRetryPolicy retryPolicy,
    IParkedMessageSink parkedMessageSink,
    TimeProvider timeProvider,
    ILoggerFactory loggerFactory,
    SubscriptionMonitor? monitor = null
) {

    /// <summary>
    /// The longest single wait: timers can't wait months (spike v1-19 T3), and a clock that jumps
    /// is noticed within it.
    /// </summary>
    internal static readonly TimeSpan MaxWait = TimeSpan.FromMinutes(1);

    static readonly TimeSpan FinalFlushTimeout = TimeSpan.FromSeconds(5);

    readonly ILogger _logger = loggerFactory.CreateLogger<Scheduler>();
    readonly DeferredCommandDispatcher _dispatcher = new(scopeFactory, retryPolicy, parkedMessageSink, loggerFactory.CreateLogger<DeferredCommandDispatcher>());
    readonly SubscriptionResubscribeOptions _resubscribeOptions = resubscribeOptions.Validate();

    /// <summary>
    /// Runs the scheduler of the policy of <paramref name="registration"/> until
    /// <paramref name="stoppingToken"/> is cancelled or a failure that is not transient occurs. It
    /// executes deferred commands, and parks a saga deadline, which a policy can't request.
    /// </summary>
    /// <param name="registration">The subscription of the handler.</param>
    /// <param name="activation">Tells whether the handler ever deferred a command.</param>
    /// <param name="stoppingToken">Stops the scheduler.</param>
    public Task RunAsync(SubscriptionRegistration registration, SchedulerActivation activation, CancellationToken stoppingToken) =>
        RunWithDeliveryAsync(registration, activation, null, stoppingToken);

    /// <summary>
    /// Runs the scheduler of the saga of <paramref name="registration"/> until
    /// <paramref name="stoppingToken"/> is cancelled or a failure that is not transient occurs. It
    /// executes deferred commands, and hands each due saga deadline to <paramref name="deliverDeadline"/>.
    /// </summary>
    /// <param name="registration">The subscription of the saga.</param>
    /// <param name="activation">Tells whether the saga ever deferred a command or requested a deadline.</param>
    /// <param name="deliverDeadline">Delivers one due deadline to the saga; it retries and parks failures itself.</param>
    /// <param name="stoppingToken">Stops the scheduler.</param>
    public Task RunAsync(SubscriptionRegistration registration, SchedulerActivation activation,
        Func<ScheduledDeadline, CancellationToken, ValueTask> deliverDeadline, CancellationToken stoppingToken) =>
        RunWithDeliveryAsync(registration, activation, deliverDeadline, stoppingToken);

    /// <summary>
    /// Runs the scheduler with the command dispatch, and with <paramref name="deliverDeadline"/> for
    /// saga deadlines; without it, a saga deadline is parked.
    /// </summary>
    Task RunWithDeliveryAsync(SubscriptionRegistration registration, SchedulerActivation activation,
        Func<ScheduledDeadline, CancellationToken, ValueTask>? deliverDeadline, CancellationToken stoppingToken) {
        var subscriptionId = ScheduledFormat.SchedulerStream(registration.SubscriptionId);
        return RunCoreAsync(registration, activation, (request, ct) => request switch {
            // A deferred command, or a request that could not be deserialized: the dispatcher executes or parks it.
            { Kind: ScheduledKind.Command } or { Payload: null } => _dispatcher.DeliverAsync(subscriptionId, request, ct),
            { Payload: { } payload } when deliverDeadline is not null =>
                deliverDeadline(new ScheduledDeadline(request.Request, request.Due, payload, request.Metadata, request.CommitPosition), ct),
            _ => _dispatcher.ParkAsync(subscriptionId, request,
                new InvalidOperationException($"Request {request.Request} is a saga deadline, but scheduler '{subscriptionId}' doesn't deliver deadlines."), ct)
        }, stoppingToken);
    }

    /// <summary>
    /// Runs the scheduler with <paramref name="deliver"/> for every due request.
    /// </summary>
    /// <param name="registration">The subscription of the handler.</param>
    /// <param name="activation">Tells whether the handler ever deferred a command.</param>
    /// <param name="deliver">Executes or delivers one due request; it retries and parks failures itself.</param>
    /// <param name="stoppingToken">Stops the scheduler.</param>
    internal async Task RunCoreAsync(SubscriptionRegistration registration, SchedulerActivation activation,
        Func<ScheduledRequest, CancellationToken, ValueTask> deliver, CancellationToken stoppingToken) {
        var subscriptionId = ScheduledFormat.SchedulerStream(registration.SubscriptionId);
        var retry = new SchedulerRetry(subscriptionFactory, _resubscribeOptions, timeProvider, _logger, subscriptionId, monitor);
        Exception? failure = null;
        monitor?.Report(subscriptionId, SubscriptionState.Waiting);
        try {
            if (!await retry.ExecuteAsync(ct => activation.IsActivatedAsync(registration, ct), "check whether its handler deferred commands", stoppingToken)) {
                _logger.LogWaiting(subscriptionId);
                // Again: a retried check reported it as running when it succeeded.
                monitor?.Report(subscriptionId, SubscriptionState.Waiting);
                await activation.Activated.WaitAsync(stoppingToken);
                _logger.LogActivated(subscriptionId);
            }
            monitor?.Report(subscriptionId, SubscriptionState.Running);
            await RunActiveAsync(registration, subscriptionId, retry, deliver, stoppingToken);
        } catch (Exception) when (stoppingToken.IsCancellationRequested) {
            // Not only OperationCanceledException: a transport may report the cancellation as its own exception.
        } catch (Exception exception) {
            failure = exception;
            _logger.LogStopped(exception, subscriptionId);
            throw;
        } finally {
            monitor?.Report(subscriptionId, SubscriptionState.Stopped, error: failure);
        }
    }

    /// <summary>
    /// Reads the checkpoint, starts the feed, and runs a <see cref="SchedulerRun"/> until the host
    /// stops or a failure ends it; the checkpoint is flushed in every case.
    /// </summary>
    async Task RunActiveAsync(SubscriptionRegistration registration, string subscriptionId, SchedulerRetry retry,
        Func<ScheduledRequest, CancellationToken, ValueTask> deliver, CancellationToken stoppingToken) {
        var start = await retry.ExecuteAsync(ct => GetStartPositionAsync(registration, subscriptionId, ct), "read its checkpoint", stoppingToken);
        _logger.LogReading(subscriptionId, start);

        var owners = new HashSet<string>([registration.SubscriptionId, .. registration.Predecessors ?? []], StringComparer.Ordinal);
        var index = new SchedulerIndex(owners, start, store.Deserialize);
        var tracker = new CheckpointTracker(checkpointStore, subscriptionId, checkpointOptions, timeProvider);
        try {
            await using var feed = SchedulerFeed.Start(store, retry, timeProvider, _logger, subscriptionId, start, stoppingToken);
            await new SchedulerRun(store, index, tracker, retry, deliver, timeProvider, _logger, subscriptionId, start).RunAsync(feed, stoppingToken);
        } finally {
            await FlushFinalCheckpointAsync(tracker, subscriptionId);
        }
    }

    /// <summary>
    /// The scheduler's own checkpoint, or the nearest predecessor's, which is adopted. Null for the
    /// beginning.
    /// </summary>
    async ValueTask<ulong?> GetStartPositionAsync(SubscriptionRegistration registration, string subscriptionId, CancellationToken cancellationToken) {
        string[] predecessors = [.. (registration.Predecessors ?? []).Select(ScheduledFormat.SchedulerStream)];
        if (await checkpointStore.GetOrAdoptAsync(subscriptionId, predecessors, cancellationToken) is not { } checkpoint)
            return null;
        if (checkpoint.AdoptedFrom == 0)
            _logger.LogContinuingFromPredecessor(subscriptionId, predecessors[0], checkpoint.Position);
        else if (checkpoint.AdoptedFrom is { } i)
            _logger.LogContinuingFromOlderPredecessor(subscriptionId, predecessors[i], checkpoint.Position, string.Join(", ", predecessors.Take(i)));
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
            _logger.LogFinalFlushFailed(exception, subscriptionId, tracker.LastPosition);
        }
    }
}
