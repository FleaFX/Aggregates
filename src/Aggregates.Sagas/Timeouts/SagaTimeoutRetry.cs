using Aggregates.Subscriptions;
using Microsoft.Extensions.Logging;

namespace Aggregates.Sagas;

/// <summary>
/// The handling of transient failures for one timeout scheduler: which failures are transient
/// (see <see cref="ISubscriptionFactory.IsTransient"/>), the backoff before the next attempt
/// (see <see cref="SubscriptionResubscribeOptions"/>), and retrying an action until it succeeds.
/// </summary>
/// <param name="subscriptionFactory">Decides which failures are transient.</param>
/// <param name="options">The backoff settings.</param>
/// <param name="timeProvider">The clock of the delays.</param>
/// <param name="logger">The logger of the scheduler.</param>
/// <param name="subscriptionId">The id of the scheduler (<c>timeouts-{saga id}</c>).</param>
sealed class SagaTimeoutRetry(
    ISubscriptionFactory subscriptionFactory,
    SubscriptionResubscribeOptions options,
    TimeProvider timeProvider,
    ILogger logger,
    string subscriptionId
) {

    /// <summary>
    /// Whether <paramref name="exception"/> is transient.
    /// </summary>
    /// <param name="exception">The failure.</param>
    public bool IsTransient(Exception exception) => subscriptionFactory.IsTransient(exception);

    /// <summary>
    /// The backoff delay after <paramref name="failures"/> consecutive failures.
    /// </summary>
    /// <param name="failures">The number of consecutive failures (1-based).</param>
    public TimeSpan Delay(int failures) =>
        Backoff.Delay(failures, options.InitialDelay, options.MaxDelay, options.BackoffMultiplier);

    /// <summary>
    /// Runs <paramref name="action"/> until it succeeds, with backoff while it fails transiently.
    /// </summary>
    /// <param name="action">The action.</param>
    /// <param name="what">What the action does, for the log ("could not {what}").</param>
    /// <param name="cancellationToken">A cancellation token.</param>
    public async ValueTask<T> ExecuteAsync<T>(Func<CancellationToken, ValueTask<T>> action, string what, CancellationToken cancellationToken) {
        for (var attempt = 1; ; attempt++) {
            try {
                return await action(cancellationToken);
            } catch (Exception exception) when (exception is not OperationCanceledException && IsTransient(exception)) {
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
                return;
            } catch (Exception exception) when (exception is not OperationCanceledException && IsTransient(exception)) {
                await BackOffAsync(exception, what, attempt, cancellationToken);
            }
        }
    }

    /// <summary>
    /// Logs a transient failure of an action and waits before its next attempt.
    /// </summary>
    async ValueTask BackOffAsync(Exception exception, string what, int attempt, CancellationToken cancellationToken) {
        var delay = Delay(attempt);
        logger.LogRetrying(exception, subscriptionId, what, delay, attempt);
        await Task.Delay(delay, timeProvider, cancellationToken);
    }
}
