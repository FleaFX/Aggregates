using System.Text;
using Aggregates.Subscriptions.Internal;
using Microsoft.Extensions.Logging;

namespace Aggregates.Subscriptions;

/// <summary>
/// One active run of a <see cref="Scheduler"/>: applies the entries of the feed to the index,
/// advances the checkpoint, and delivers the due requests once it read back its own marker.
/// Not thread-safe.
/// </summary>
/// <param name="store">The store to append the marker and the deliveries to.</param>
/// <param name="index">The open requests of the handler.</param>
/// <param name="tracker">The checkpoint of the scheduler.</param>
/// <param name="retry">The handling of transient failures.</param>
/// <param name="deliver">Executes one due request; it retries and parks failures itself.</param>
/// <param name="timeProvider">The clock.</param>
/// <param name="logger">The logger of the scheduler.</param>
/// <param name="subscriptionId">The id of the scheduler (<c>scheduled-{id}</c>).</param>
/// <param name="start">The position the feed reads after; null from the beginning.</param>
sealed class SchedulerRun(
    IScheduledStore store,
    SchedulerIndex index,
    CheckpointTracker tracker,
    SchedulerRetry retry,
    Func<ScheduledRequest, CancellationToken, ValueTask> deliver,
    TimeProvider timeProvider,
    ILogger logger,
    string subscriptionId,
    ulong? start
) {

    readonly byte[] _marker = Encoding.UTF8.GetBytes(Guid.NewGuid().ToString("N"));
    bool _caughtUp;
    ulong? _stored = start;
    int _deliveryFailures;

    /// <summary>
    /// Appends the marker, then runs until <paramref name="feed"/> ends or
    /// <paramref name="stoppingToken"/> is cancelled.
    /// </summary>
    /// <param name="feed">The feed, started before the marker is appended.</param>
    /// <param name="stoppingToken">Stops the run.</param>
    public async Task RunAsync(SchedulerFeed feed, CancellationToken stoppingToken) {
        // Appended after the feed started: once the feed delivers it, the index holds every request stored before.
        await retry.ExecuteAsync(ct => store.AppendAsync(subscriptionId, ScheduledFormat.MarkerEventType, _marker, ct), "append its marker", stoppingToken);

        while (true) {
            ApplyEntries(feed);
            await AdvanceCheckpointAsync(stoppingToken);

            var now = timeProvider.GetUtcNow();
            if (_caughtUp && index.TryPeekDue(now, out var due)) {
                await DeliverAsync(due, stoppingToken);
                continue;
            }

            var wait = _caughtUp && index.NextDue is { } next && next - now < Scheduler.MaxWait ? next - now : Scheduler.MaxWait;
            if (wait > TimeSpan.Zero && !await feed.WaitAsync(wait, stoppingToken))
                return;
        }
    }

    /// <summary>
    /// Applies every buffered entry of <paramref name="feed"/> to the index.
    /// </summary>
    void ApplyEntries(SchedulerFeed feed) {
        while (feed.TryRead(out var entry)) {
            switch (index.Apply(entry, out var requested)) {
                case SchedulerChange.Requested when requested.Error is { } error:
                    logger.LogUndeserializable(error, subscriptionId, requested.Kind == ScheduledKind.Deadline ? "deadline payload" : "command",
                        requested.Request, requested.Request.StreamId, requested.Due);
                    break;
                case SchedulerChange.Requested:
                    logger.LogRequested(subscriptionId, requested.Request, requested.Request.StreamId, requested.Due, requested.Payload!.GetType().Name);
                    break;
                case SchedulerChange.Invalid:
                    logger.LogInvalidRequest(subscriptionId, entry.Source);
                    break;
                case SchedulerChange.Marker when !_caughtUp && IsOwnMarker(entry):
                    _caughtUp = true;
                    logger.LogCaughtUp(subscriptionId, index.Count, index.NextDue);
                    break;
            }
        }
    }

    /// <summary>
    /// Whether <paramref name="entry"/> is the marker of this run; earlier runs and the activation
    /// write markers to the same stream.
    /// </summary>
    bool IsOwnMarker(ScheduledEntry entry) =>
        entry.Source.StreamId == subscriptionId && entry.Data.Span.SequenceEqual(_marker);

    /// <summary>
    /// Records the index's checkpoint when it rose.
    /// </summary>
    async ValueTask AdvanceCheckpointAsync(CancellationToken cancellationToken) {
        if (index.Checkpoint is { } checkpoint && (_stored is null || checkpoint > _stored)) {
            await tracker.AdvanceAsync(checkpoint, cancellationToken);
            _stored = checkpoint;
        }
    }

    /// <summary>
    /// Delivers <paramref name="due"/> and records it as handled. After a transient failure it
    /// waits and returns without recording, so the next pass applies new entries first.
    /// </summary>
    async ValueTask DeliverAsync(ScheduledRequest due, CancellationToken cancellationToken) {
        try {
            await deliver(due, cancellationToken);
            if (_deliveryFailures > 0) {
                retry.Recovered();
                _deliveryFailures = 0;
            }
        } catch (Exception exception) when (!cancellationToken.IsCancellationRequested && retry.IsTransient(exception)) {
            var delay = retry.Failed(++_deliveryFailures, exception);
            logger.LogDeliveryFailed(exception, subscriptionId, due.Request, delay, _deliveryFailures);
            await Task.Delay(delay, timeProvider, cancellationToken);
            return;
        }
        // Recorded after the command, so a crash in between runs the command again (at least once).
        await retry.ExecuteAsync(ct => store.AppendAsync(subscriptionId, ScheduledFormat.DeliveryEventType, due.Request.ToLinkData(), ct), "record a delivery", cancellationToken);
        index.Remove(due.Request);
    }
}
