namespace Aggregates.Subscriptions;

/// <summary>
/// Computes exponential backoff delays with ±10% jitter, for retries and resubscribes.
/// </summary>
public static class Backoff {
    /// <summary>
    /// Returns the delay before the next attempt, after <paramref name="attempt"/> failed attempts.
    /// </summary>
    /// <param name="attempt">The number of failed attempts so far; 1-based.</param>
    /// <param name="initialDelay">The delay after the first failed attempt.</param>
    /// <param name="maxDelay">The upper bound before jitter.</param>
    /// <param name="multiplier">The factor applied to the delay after each failed attempt.</param>
    /// <returns>The delay to wait before the next attempt.</returns>
    public static TimeSpan Delay(int attempt, TimeSpan initialDelay, TimeSpan maxDelay, double multiplier) {
        var ms = Math.Min(
            initialDelay.TotalMilliseconds * Math.Pow(multiplier, attempt - 1),
            maxDelay.TotalMilliseconds);
        // ±10% jitter to avoid thundering herd on burst failures.
        var jitter = ms * 0.1 * (Random.Shared.NextDouble() * 2.0 - 1.0);
        return TimeSpan.FromMilliseconds(ms + jitter);
    }
}
