using System.Diagnostics;

namespace Aggregates.Testing;

/// <summary>
/// Waits for asynchronous effects of subscriptions without fixed sleeps.
/// </summary>
static class Eventually {
    static readonly TimeSpan PollInterval = TimeSpan.FromMilliseconds(50);

    /// <summary>
    /// The default time to wait for a condition to become true.
    /// </summary>
    public static readonly TimeSpan DefaultTimeout = TimeSpan.FromSeconds(15);

    /// <summary>
    /// How long to wait before asserting that nothing (more) happens.
    /// </summary>
    public static readonly TimeSpan QuietPeriod = TimeSpan.FromSeconds(2);

    /// <summary>
    /// Polls <paramref name="condition"/> until it returns <see langword="true"/>, and fails the
    /// test when <paramref name="timeout"/> elapses first.
    /// </summary>
    public static Task UntilAsync(Func<bool> condition, string because, TimeSpan? timeout = null) =>
        UntilAsync(() => ValueTask.FromResult(condition()), because, timeout);

    /// <summary>
    /// Polls <paramref name="condition"/> until it returns <see langword="true"/>, and fails the
    /// test when <paramref name="timeout"/> elapses first.
    /// </summary>
    public static async Task UntilAsync(Func<ValueTask<bool>> condition, string because, TimeSpan? timeout = null) {
        var limit = timeout ?? DefaultTimeout;
        var stopwatch = Stopwatch.StartNew();
        while (true) {
            if (await condition())
                return;
            if (stopwatch.Elapsed >= limit)
                Assert.Fail($"Condition not met within {limit.TotalSeconds:0.#} s: {because}");
            await Task.Delay(PollInterval, TestContext.Current.CancellationToken);
        }
    }

    /// <summary>
    /// Waits for <see cref="QuietPeriod"/>, so that a following assert can check that nothing
    /// else happened in the meantime.
    /// </summary>
    public static Task QuietAsync() =>
        Task.Delay(QuietPeriod, TestContext.Current.CancellationToken);
}
