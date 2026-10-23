using Aggregates.Subscriptions.Internal;
using Microsoft.Extensions.Logging;

namespace Aggregates.Subscriptions;

/// <summary>
/// The handling of transient failures for one scheduler: which failures are transient
/// (see <see cref="ISubscriptionFactory.IsTransient"/>), the backoff before the next attempt
/// (see <see cref="SubscriptionResubscribeOptions"/>), retrying an action until it succeeds, and
/// reporting the scheduler as retrying or running again to the <see cref="SubscriptionMonitor"/>.
/// </summary>
/// <param name="subscriptionFactory">Decides which failures are transient.</param>
/// <param name="options">The backoff settings.</param>
/// <param name="timeProvider">The clock of the delays.</param>
/// <param name="logger">The logger of the scheduler.</param>
/// <param name="subscriptionId">The id of the scheduler (<c>scheduled-{id}</c>).</param>
/// <param name="monitor">Receives the state of the scheduler; optional.</param>
sealed class SchedulerRetry(
    ISubscriptionFactory subscriptionFactory,
    SubscriptionResubscribeOptions options,
    TimeProvider timeProvider,
    ILogger logger,
    string subscriptionId,
    SubscriptionMonitor? monitor
) {

    /// <summary>
    /// Whether <paramref name="exception"/> is transient.
    /// </summary>
    /// <param name="exception">The failure.</param>
    public bool IsTransient(Exception exception) => subscriptionFactory.IsTransient(exception);

    /// <summary>
    /// Reports the scheduler as retrying after <paramref name="failures"/> consecutive failures, and
    /// returns the backoff delay before the next attempt.
    /// </summary>
    /// <param name="failures">The number of consecutive failures (1-based).</param>
    /// <param name="exception">The latest failure; null when the feed ended without one.</param>
    public TimeSpan Failed(int failures, Exception? exception) {
        monitor?.Report(subscriptionId, SubscriptionState.Retrying, attempts: failures, error: exception);
        return Backoff.Delay(failures, options.InitialDelay, options.MaxDelay, options.BackoffMultiplier);
    }

    /// <summary>
    /// Reports the scheduler as running again after a failure.
    /// </summary>
    public void Recovered() => monitor?.Report(subscriptionId, SubscriptionState.Running);

    /// <summary>
    /// Runs <paramref name="action"/> until it succeeds, with backoff while it fails transiently.
    /// </summary>
    /// <param name="action">The action.</param>
    /// <param name="what">What the action does, for the log ("could not {what}").</param>
    /// <param name="cancellationToken">A cancellation token.</param>
    public async ValueTask<T> ExecuteAsync<T>(Func<CancellationToken, ValueTask<T>> action, string what, CancellationToken cancellationToken) {
        for (var attempt = 1; ; attempt++) {
            try {
                var result = await action(cancellationToken);
                if (attempt > 1)
                    Recovered();
                return result;
            } catch (Exception exception) when (!cancellationToken.IsCancellationRequested && IsTransient(exception)) {
                await BackOffAsync(exception, what, attempt, cancellationToken);
            }
        }
    }

    /// <summary>
    /// Runs <paramref name="action"/> until it succeeds, with backoff while it fails transiently.
    /// </summary>
    /// <param name="action">The action.</param>
    /// <param name="what">What the action does, for the log ("could not {what}").</param>
    /// <param name="cancellationToken">A cancellation token.</param>
    public async ValueTask ExecuteAsync(Func<CancellationToken, ValueTask> action, string what, CancellationToken cancellationToken) {
        for (var attempt = 1; ; attempt++) {
            try {
                await action(cancellationToken);
                if (attempt > 1)
                    Recovered();
                return;
            } catch (Exception exception) when (!cancellationToken.IsCancellationRequested && IsTransient(exception)) {
                await BackOffAsync(exception, what, attempt, cancellationToken);
            }
        }
    }

    /// <summary>
    /// Logs a transient failure of an action and waits before its next attempt.
    /// </summary>
    async ValueTask BackOffAsync(Exception exception, string what, int attempt, CancellationToken cancellationToken) {
        var delay = Failed(attempt, exception);
        logger.LogRetrying(exception, subscriptionId, what, delay, attempt);
        await Task.Delay(delay, timeProvider, cancellationToken);
    }
}
