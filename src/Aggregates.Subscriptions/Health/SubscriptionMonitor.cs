using System.Collections.Concurrent;

namespace Aggregates.Subscriptions;

/// <summary>
/// Keeps the state of every subscription and scheduler of a host. The <see cref="SubscriptionLoop"/>,
/// the <see cref="Scheduler"/> and the <see cref="SubscriptionRetryPolicy"/> report each change of
/// state; the <see cref="SubscriptionHealthCheck"/> reads them. Registered as a singleton by
/// <c>AddProjections</c>, <c>AddPolicies</c> and <c>AddSagas</c>.
/// </summary>
/// <remarks>
/// A subscription or scheduler is listed from the moment it starts. Changes are reported when they
/// happen, never for every processed message.
/// </remarks>
/// <param name="timeProvider">The clock of <see cref="SubscriptionStatus.Since"/>.</param>
public sealed class SubscriptionMonitor(TimeProvider timeProvider) {
    readonly ConcurrentDictionary<string, SubscriptionStatus> _statuses = new(StringComparer.Ordinal);

    /// <summary>
    /// The state of every subscription and scheduler that started in this host, ordered by id.
    /// </summary>
    public IReadOnlyList<SubscriptionStatus> GetStatuses() =>
        [.. _statuses.Values.OrderBy(status => status.Id, StringComparer.Ordinal)];

    /// <summary>
    /// Records the state of <paramref name="id"/>. <see cref="SubscriptionStatus.Since"/> stays as it
    /// was when the state is the same as before.
    /// </summary>
    /// <param name="id">The subscription id, or the id of a scheduler.</param>
    /// <param name="state">The state.</param>
    /// <param name="position">The commit position, if known.</param>
    /// <param name="attempts">The number of consecutive failed attempts.</param>
    /// <param name="error">The latest failure.</param>
    internal void Report(string id, SubscriptionState state, ulong? position = null, int attempts = 0, Exception? error = null) {
        var now = timeProvider.GetUtcNow();
        _statuses.AddOrUpdate(id,
            key => new SubscriptionStatus(key, state, now, position, attempts, error),
            (key, current) => new SubscriptionStatus(key, state, current.State == state ? current.Since : now, position, attempts, error));
    }
}
