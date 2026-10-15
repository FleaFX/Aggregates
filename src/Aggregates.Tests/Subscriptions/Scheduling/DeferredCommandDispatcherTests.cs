using Aggregates.Sagas;
using FakeItEasy;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Logging.Testing;

namespace Aggregates.Subscriptions;

public class DeferredCommandDispatcherTests {
    static readonly EventMetadata RequestMetadata = new(new Dictionary<string, object?> { ["correlation"] = "c-1" });

    static ScheduledRequest Request(ICommand? command, Exception? error = null) =>
        new(new EventReference("saga-1", 3), DateTimeOffset.UtcNow, command, error, RequestMetadata, 42, 41);

    static (DeferredCommandDispatcher Dispatcher, IServiceProvider Provider, IParkedMessageSink Parked, FakeLogger<DeferredCommandDispatcher> Logger) BuildDispatcher(ICommandDispatcher commandDispatcher) {
        var parked = A.Fake<IParkedMessageSink>();
        var services = new ServiceCollection()
            .AddScoped<ICommandDispatcher>(_ => commandDispatcher)
            .AddSingleton(parked);
        var provider = services.BuildServiceProvider();
        var logger = new FakeLogger<DeferredCommandDispatcher>();
        var retryPolicy = new SubscriptionRetryPolicy(parked, new SubscriptionErrorHandlingOptions { MaxRetries = 2, InitialDelay = TimeSpan.FromMilliseconds(1) });
        return (new DeferredCommandDispatcher(provider.GetRequiredService<IServiceScopeFactory>(), retryPolicy, parked, logger), provider, parked, logger);
    }

    [Fact]
    public async Task DispatchesTheCommand_InAScope_WithTheMetadataOfTheRequest() {
        var command = new TestCommand();
        EventMetadata? capturedMetadata = null;
        var commandDispatcher = A.Fake<ICommandDispatcher>();
        A.CallTo(() => commandDispatcher.DispatchAsync(A<ICommand>._, A<CancellationToken>._))
            .Invokes(_ => capturedMetadata = MetadataScope.Current?.Snapshot());
        var (dispatcher, _, parked, _) = BuildDispatcher(commandDispatcher);

        await dispatcher.DeliverAsync("scheduled-sub-1", Request(command), TestContext.Current.CancellationToken);

        A.CallTo(() => commandDispatcher.DispatchAsync(command, A<CancellationToken>._)).MustHaveHappenedOnceExactly();
        capturedMetadata.Should().NotBeNull();
        capturedMetadata!["correlation"].Should().Be("c-1");
        A.CallTo(() => parked.ParkAsync(A<string>._, A<SubscriptionMessage>._, A<Exception>._, A<CancellationToken>._))
            .MustNotHaveHappened();
    }

    [Fact]
    public async Task AFailingCommand_IsParked_UnderTheSchedulerId() {
        var commandDispatcher = A.Fake<ICommandDispatcher>();
        A.CallTo(() => commandDispatcher.DispatchAsync(A<ICommand>._, A<CancellationToken>._))
            .Throws(new InvalidOperationException("no handler"));
        var (dispatcher, _, parked, _) = BuildDispatcher(commandDispatcher);
        var request = Request(new TestCommand());

        await dispatcher.DeliverAsync("scheduled-sub-1", request, TestContext.Current.CancellationToken);

        A.CallTo(() => commandDispatcher.DispatchAsync(A<ICommand>._, A<CancellationToken>._)).MustHaveHappened(2, Times.Exactly);
        A.CallTo(() => parked.ParkAsync("scheduled-sub-1",
            A<SubscriptionMessage>.That.Matches(message => message.CommitPosition == 42 && message.Source == request.Request),
            A<InvalidOperationException>._, A<CancellationToken>._)).MustHaveHappenedOnceExactly();
    }

    [Fact]
    public async Task AnUndeserializableCommand_IsParkedRightAway() {
        var commandDispatcher = A.Fake<ICommandDispatcher>();
        var (dispatcher, _, parked, logger) = BuildDispatcher(commandDispatcher);
        var error = new InvalidOperationException("Deserialize does not know the command type 'Test.Unknown@v1'.");
        var request = Request(null, error);

        await dispatcher.DeliverAsync("scheduled-sub-1", request, TestContext.Current.CancellationToken);

        A.CallTo(() => commandDispatcher.DispatchAsync(A<ICommand>._, A<CancellationToken>._)).MustNotHaveHappened();
        A.CallTo(() => parked.ParkAsync("scheduled-sub-1",
            A<SubscriptionMessage>.That.Matches(message => message.CommitPosition == 42 && message.Source == request.Request),
            error, A<CancellationToken>._)).MustHaveHappenedOnceExactly();
        logger.Collector.GetSnapshot().Should().Contain(record =>
            record.Level == LogLevel.Warning && record.Message.Contains("Parking deferred command"));
    }
}
