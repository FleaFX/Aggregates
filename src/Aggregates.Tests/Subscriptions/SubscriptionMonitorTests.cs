using FluentAssertions;
using Microsoft.Extensions.Time.Testing;

namespace Aggregates.Subscriptions;

public class SubscriptionMonitorTests {
    readonly FakeTimeProvider _time = new(new DateTimeOffset(2026, 10, 20, 8, 0, 0, TimeSpan.Zero));

    SubscriptionMonitor Monitor() => new(_time);

    [Fact]
    public void Report_RecordsTheStatus_AtTheTimeOfTheReport() {
        var monitor = Monitor();
        var error = new InvalidOperationException("boom");

        monitor.Report("sub-1", SubscriptionState.Blocked, 42, 3, error);

        monitor.GetStatuses().Should().Equal(new SubscriptionStatus { Id = "sub-1", State = SubscriptionState.Blocked, Since = _time.GetUtcNow(), Position = 42, Attempts = 3, Error = error });
    }

    [Fact]
    public void Report_InTheSameState_KeepsSince_AndReplacesTheRest() {
        var monitor = Monitor();
        var first = new InvalidOperationException("first");
        var second = new InvalidOperationException("second");

        monitor.Report("sub-1", SubscriptionState.Retrying, 1, 1, first);
        var since = _time.GetUtcNow();
        _time.Advance(TimeSpan.FromMinutes(1));
        monitor.Report("sub-1", SubscriptionState.Retrying, 2, 2, second);

        var status = monitor.GetStatuses().Should().ContainSingle().Which;
        status.Since.Should().Be(since);
        status.Position.Should().Be(2);
        status.Attempts.Should().Be(2);
        status.Error.Should().BeSameAs(second);
    }

    [Fact]
    public void Report_InAnotherState_StartsSinceAgain() {
        var monitor = Monitor();
        monitor.Report("sub-1", SubscriptionState.Retrying);
        _time.Advance(TimeSpan.FromMinutes(1));

        monitor.Report("sub-1", SubscriptionState.Running);

        var status = monitor.GetStatuses().Should().ContainSingle().Which;
        status.Since.Should().Be(_time.GetUtcNow());
        status.Attempts.Should().Be(0);
        status.Error.Should().BeNull();
    }

    [Fact]
    public void GetStatuses_IsOrderedById() {
        var monitor = Monitor();
        monitor.Report("b", SubscriptionState.Running);
        monitor.Report("B", SubscriptionState.Running);
        monitor.Report("a", SubscriptionState.Running);

        monitor.GetStatuses().Select(status => status.Id).Should().Equal("B", "a", "b");
    }

    [Fact]
    public void GetStatuses_IsASnapshot() {
        var monitor = Monitor();
        monitor.Report("sub-1", SubscriptionState.Running);

        var before = monitor.GetStatuses();

        monitor.Report("sub-2", SubscriptionState.Running);
        before.Should().ContainSingle();
        monitor.GetStatuses().Should().HaveCount(2);
    }
}
