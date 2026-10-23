using System.Threading.Channels;
using Microsoft.Extensions.Logging;

namespace Aggregates.Subscriptions;

/// <summary>
/// Reads the scheduled entries of the store in the background and buffers them until the scheduler
/// takes them. It subscribes again after a transient failure or an unexpected end, after the last
/// position it read; any other failure ends it. Disposing it stops the reading.
/// </summary>
sealed class SchedulerFeed : IAsyncDisposable {
    // From this many consecutive failures on, a resubscribe is logged as an error instead of a warning.
    const int ErrorLevelFailures = 5;

    readonly Channel<ScheduledEntry> _channel =
        Channel.CreateUnbounded<ScheduledEntry>(new UnboundedChannelOptions { SingleReader = true, SingleWriter = true });
    readonly IScheduledStore _store;
    readonly SchedulerRetry _retry;
    readonly TimeProvider _timeProvider;
    readonly ILogger _logger;
    readonly string _subscriptionId;
    readonly CancellationTokenSource _stop;
    readonly Task _reading;

    /// <summary>
    /// Starts reading after <paramref name="start"/>; every field is set before the reading starts.
    /// </summary>
    SchedulerFeed(
        IScheduledStore store,
        SchedulerRetry retry,
        TimeProvider timeProvider,
        ILogger logger,
        string subscriptionId,
        ulong? start,
        CancellationToken stoppingToken
    ) {
        _store = store;
        _retry = retry;
        _timeProvider = timeProvider;
        _logger = logger;
        _subscriptionId = subscriptionId;
        _stop = CancellationTokenSource.CreateLinkedTokenSource(stoppingToken);
        _reading = ReadAsync(start, _stop.Token);
    }

    /// <summary>
    /// Starts a feed that reads the scheduled entries after <paramref name="start"/>.
    /// </summary>
    /// <param name="store">The store of the scheduled entries.</param>
    /// <param name="retry">The handling of transient failures.</param>
    /// <param name="timeProvider">The clock of the delays.</param>
    /// <param name="logger">The logger of the scheduler.</param>
    /// <param name="subscriptionId">The id of the scheduler (<c>scheduled-{id}</c>).</param>
    /// <param name="start">The position to read after; null from the beginning.</param>
    /// <param name="stoppingToken">Stops the reading, like <see cref="DisposeAsync"/>.</param>
    public static SchedulerFeed Start(
        IScheduledStore store,
        SchedulerRetry retry,
        TimeProvider timeProvider,
        ILogger logger,
        string subscriptionId,
        ulong? start,
        CancellationToken stoppingToken
    ) =>
        new(store, retry, timeProvider, logger, subscriptionId, start, stoppingToken);

    /// <summary>
    /// Takes the next buffered entry, if any.
    /// </summary>
    /// <param name="entry">The entry.</param>
    public bool TryRead(out ScheduledEntry entry) => _channel.Reader.TryRead(out entry);

    /// <summary>
    /// Waits until an entry can be taken or <paramref name="timeout"/> has passed.
    /// </summary>
    /// <param name="timeout">The longest wait.</param>
    /// <param name="stoppingToken">Stops the wait.</param>
    /// <returns><see langword="false"/> when the feed ended; a failure that ended it is rethrown.</returns>
    public async ValueTask<bool> WaitAsync(TimeSpan timeout, CancellationToken stoppingToken) {
        using var timer = new CancellationTokenSource(timeout, _timeProvider);
        using var wake = CancellationTokenSource.CreateLinkedTokenSource(stoppingToken, timer.Token);
        try {
            if (await _channel.Reader.WaitToReadAsync(wake.Token))
                return true;
        } catch (OperationCanceledException) when (!stoppingToken.IsCancellationRequested) {
            // The wait ended: a request may be due now.
            return true;
        }
        await _reading;
        return false;
    }

    /// <summary>
    /// Stops the reading and waits until it has stopped.
    /// </summary>
    public async ValueTask DisposeAsync() {
        await _stop.CancelAsync();
        try {
            await _reading;
        } catch (Exception) {
            // Already rethrown by WaitAsync, or cancelled.
        }
        _stop.Dispose();
    }

    /// <summary>
    /// Reads the store into the channel, subscribing again after a transient failure or an unexpected end.
    /// </summary>
    async Task ReadAsync(ulong? start, CancellationToken cancellationToken) {
        var from = start;
        var failures = 0;
        try {
            while (true) {
                TimeSpan delay;
                try {
                    await foreach (var entry in _store.SubscribeAsync(from, cancellationToken)) {
                        await _channel.Writer.WriteAsync(entry, cancellationToken);
                        if (!entry.IsProgress || entry.CommitPosition > (from ?? 0))
                            from = entry.CommitPosition;
                        if (failures > 0) {
                            _retry.Recovered();
                            failures = 0;
                        }
                    }
                    delay = _retry.Failed(++failures, exception: null);
                    _logger.LogFeedEnded(_subscriptionId, from, delay, failures);
                } catch (Exception) when (cancellationToken.IsCancellationRequested) {
                    // Not only OperationCanceledException: a transport may report the cancellation as its own exception.
                    return;
                } catch (Exception exception) when (_retry.IsTransient(exception)) {
                    delay = _retry.Failed(++failures, exception);
                    _logger.LogFeedFailed(failures >= ErrorLevelFailures ? LogLevel.Error : LogLevel.Warning, exception, _subscriptionId, from, delay, failures);
                }

                try {
                    await Task.Delay(delay, _timeProvider, cancellationToken);
                } catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) {
                    return;
                }
            }
        } finally {
            _channel.Writer.TryComplete();
        }
    }
}
