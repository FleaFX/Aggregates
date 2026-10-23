using Aggregates.Subscriptions;
using FakeItEasy;
using FluentAssertions;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Logging.Testing;

namespace Aggregates.Policies;

public class LoggingPolicyHandlerTests {
    readonly IPolicy<PolicyTestEvent> _policy = A.Fake<IPolicy<PolicyTestEvent>>();
    readonly FakeLogger<LoggingPolicyHandler<IPolicy<PolicyTestEvent>, PolicyTestEvent>> _logger = new();

    public LoggingPolicyHandlerTests() =>
        A.CallTo(() => _policy.ReactAsync(A<PolicyTestEvent>._, A<CancellationToken>._))
            .Returns(AsyncEnumerable.Empty<ICommand>());

    [Fact]
    public async Task GivenSuccess_LogsHandlingAndHandled() {
        await BuildHandler().HandleAsync(new PolicyTestEvent(1), new EventReference("src-1", 0), TestContext.Current.CancellationToken);

        _logger.Collector.GetSnapshot()
            .Should().Contain(r => r.Level == LogLevel.Debug && r.Message.Contains("Handling"))
            .And.Contain(r => r.Level == LogLevel.Debug && r.Message.Contains("Handled"));
    }

    [Fact]
    public async Task LogsTheEventTypeAndThePolicy() {
        await BuildHandler().HandleAsync(new PolicyTestEvent(1), new EventReference("src-1", 0), TestContext.Current.CancellationToken);

        _logger.Collector.GetSnapshot()
            .Should().Contain(r => r.Message == "Handling PolicyTestEvent in IPolicy`1");
    }

    [Fact]
    public async Task GivenException_LogsErrorAndRethrows() {
        A.CallTo(() => _policy.ReactAsync(A<PolicyTestEvent>._, A<CancellationToken>._))
            .Throws<InvalidOperationException>();

        var act = () => BuildHandler().HandleAsync(new PolicyTestEvent(1), new EventReference("src-1", 0), TestContext.Current.CancellationToken).AsTask();

        await act.Should().ThrowAsync<InvalidOperationException>();
        _logger.Collector.GetSnapshot()
            .Should().Contain(r => r.Level == LogLevel.Warning);
    }

    [Fact]
    public async Task HandleAsync_WhenCancelled_LogsNoWarning() {
        using var cts = new CancellationTokenSource();
        cts.Cancel();
        A.CallTo(() => _policy.ReactAsync(A<PolicyTestEvent>._, A<CancellationToken>._))
            .Throws(new OperationCanceledException(cts.Token));

        var act = () => BuildHandler().HandleAsync(new PolicyTestEvent(1), new EventReference("src-1", 0), cts.Token).AsTask();

        await act.Should().ThrowAsync<OperationCanceledException>();
        _logger.Collector.GetSnapshot()
            .Should().NotContain(r => r.Level == LogLevel.Warning);
    }

    // The interface itself as TPolicy, so the policy can be faked.
    LoggingPolicyHandler<IPolicy<PolicyTestEvent>, PolicyTestEvent> BuildHandler() {
        var store = A.Fake<IScheduledStore>();
        return new(new PolicyHandler<IPolicy<PolicyTestEvent>, PolicyTestEvent>(_policy, A.Fake<ICommandDispatcher>(),
            new SchedulerActivation<IPolicy<PolicyTestEvent>>(store, []), store,
            NullLogger<PolicyHandler<IPolicy<PolicyTestEvent>, PolicyTestEvent>>.Instance), _logger);
    }
}
