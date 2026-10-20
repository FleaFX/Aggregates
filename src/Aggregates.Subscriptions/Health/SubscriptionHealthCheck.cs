using System.Globalization;
using Microsoft.Extensions.Diagnostics.HealthChecks;

namespace Aggregates.Subscriptions;

/// <summary>
/// Reports the state of every subscription and scheduler of the host (see <see cref="SubscriptionMonitor"/>):
/// unhealthy when one stopped, degraded when one is retrying or blocked, healthy otherwise. Register it
/// with <c>services.AddHealthChecks().AddCheck&lt;SubscriptionHealthCheck&gt;("aggregates")</c>.
/// </summary>
/// <remarks>
/// The description names the subscriptions and schedulers that need attention; the data holds one line
/// per id, such as <c>Blocked since 2026-10-20T08:00:00.0000000+00:00 at position 42 (attempt 7): Timeout</c>.
/// A subscription stays retrying until a message arrives after the failure, and every subscription and
/// scheduler reports stopped while the host stops.
/// </remarks>
/// <param name="monitor">The states of the subscriptions and schedulers.</param>
public sealed class SubscriptionHealthCheck(SubscriptionMonitor monitor) : IHealthCheck {
    /// <inheritdoc/>
    public Task<HealthCheckResult> CheckHealthAsync(HealthCheckContext context, CancellationToken cancellationToken = default) {
        var statuses = monitor.GetStatuses();
        var attention = statuses
            .Where(status => status.State is SubscriptionState.Retrying or SubscriptionState.Blocked or SubscriptionState.Stopped)
            .ToArray();
        var health = attention.Any(status => status.State == SubscriptionState.Stopped) ? HealthStatus.Unhealthy
            : attention.Length > 0 ? HealthStatus.Degraded
            : HealthStatus.Healthy;
        var description = attention.Length == 0
            ? $"{statuses.Count} subscriptions and schedulers are running or waiting."
            : $"{attention.Length} of {statuses.Count} subscriptions and schedulers need attention: " +
              string.Join("; ", attention.Select(status => $"'{status.Id}' is {status.State.ToString().ToLowerInvariant()}"));
        var data = statuses.ToDictionary(status => status.Id, status => (object)Describe(status), StringComparer.Ordinal);
        return Task.FromResult(new HealthCheckResult(health, description, data: data));
    }

    // One line per subscription or scheduler, for the data of the result.
    static string Describe(SubscriptionStatus status) {
        var line = string.Create(CultureInfo.InvariantCulture, $"{status.State} since {status.Since:O}");
        if (status.Position is { } position)
            line += string.Create(CultureInfo.InvariantCulture, $" at position {position}");
        if (status.Attempts > 0)
            line += string.Create(CultureInfo.InvariantCulture, $" (attempt {status.Attempts})");
        if (status.Error is { } error)
            line += $": {error.Message}";
        return line;
    }
}
