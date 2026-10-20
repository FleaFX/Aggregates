using Aggregates.Projections;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Microsoft.Extensions.Time.Testing;

namespace Aggregates.Subscriptions;

public class SubscriptionHealthCheckTests {
    readonly FakeTimeProvider _time = new(new DateTimeOffset(2026, 10, 20, 8, 0, 0, TimeSpan.Zero));

    SubscriptionMonitor Monitor() => new(_time);

    static Task<HealthCheckResult> CheckAsync(SubscriptionMonitor monitor) =>
        new SubscriptionHealthCheck(monitor).CheckHealthAsync(new HealthCheckContext(), TestContext.Current.CancellationToken);

    [Fact]
    public async Task WithoutStatuses_IsHealthy() {
        var result = await CheckAsync(Monitor());

        result.Status.Should().Be(HealthStatus.Healthy);
        result.Data.Should().BeEmpty();
    }

    [Fact]
    public async Task GivenRunningAndWaiting_IsHealthy_AndDescribesEach() {
        var monitor = Monitor();
        monitor.Report("sub-1", SubscriptionState.Running);
        monitor.Report("scheduled-sub-1", SubscriptionState.Waiting);

        var result = await CheckAsync(monitor);

        result.Status.Should().Be(HealthStatus.Healthy);
        result.Description.Should().Be("2 subscriptions and schedulers are running or waiting.");
        result.Data.Keys.Should().Equal("scheduled-sub-1", "sub-1");
        result.Data["sub-1"].Should().Be("Running since 2026-10-20T08:00:00.0000000+00:00");
    }

    [Theory]
    [InlineData(SubscriptionState.Retrying, "retrying")]
    [InlineData(SubscriptionState.Blocked, "blocked")]
    public async Task GivenOneRetryingOrBlocked_IsDegraded(SubscriptionState state, string word) {
        var monitor = Monitor();
        monitor.Report("sub-1", SubscriptionState.Running);
        monitor.Report("sub-2", state, 7, 1, new InvalidOperationException("boom"));

        var result = await CheckAsync(monitor);

        result.Status.Should().Be(HealthStatus.Degraded);
        result.Description.Should().Be($"1 of 2 subscriptions and schedulers need attention: 'sub-2' is {word}");
    }

    [Fact]
    public async Task GivenOneStopped_IsUnhealthy_EvenWithABlockedOne() {
        var monitor = Monitor();
        monitor.Report("sub-1", SubscriptionState.Blocked, 1, 3, new InvalidOperationException("boom"));
        monitor.Report("sub-2", SubscriptionState.Stopped);

        var result = await CheckAsync(monitor);

        result.Status.Should().Be(HealthStatus.Unhealthy);
        result.Description.Should().Contain("'sub-1' is blocked; 'sub-2' is stopped");
    }

    [Fact]
    public async Task DescribesStateSincePositionAttemptAndError() {
        var monitor = Monitor();
        monitor.Report("sub-1", SubscriptionState.Blocked, 42, 7, new InvalidOperationException("boom"));

        var result = await CheckAsync(monitor);

        result.Data["sub-1"].Should().Be("Blocked since 2026-10-20T08:00:00.0000000+00:00 at position 42 (attempt 7): boom");
    }

    [Fact]
    public async Task ResolvesThroughAddHealthChecks() {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddProjections(o => o.ScanTypes());
        services.AddHealthChecks().AddCheck<SubscriptionHealthCheck>("aggregates");
        await using var provider = services.BuildServiceProvider();

        var report = await provider.GetRequiredService<HealthCheckService>().CheckHealthAsync(TestContext.Current.CancellationToken);

        report.Entries["aggregates"].Status.Should().Be(HealthStatus.Healthy);
        provider.GetRequiredService<SubscriptionMonitor>().Should().BeSameAs(provider.GetRequiredService<SubscriptionMonitor>());
    }
}
