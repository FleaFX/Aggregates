using Aggregates.Subscriptions;
using FakeItEasy;
using FluentAssertions;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Testing;

namespace Aggregates.Subscriptions;

public class SubscriptionRetryPolicyTests {

    public class ExecuteAsync {
        static readonly SubscriptionMessage AnyMessage =
            new(Event: null, CommitPosition: 42UL, Source: new EventReference("stream-1", 42), Metadata: EventMetadata.Empty);

        static SubscriptionRetryPolicy BuildPolicy(IParkedMessageSink sink, int maxRetries = 3) =>
            new(sink, new SubscriptionErrorHandlingOptions {
                MaxRetries = maxRetries,
                InitialDelay = TimeSpan.Zero,
                MaxDelay = TimeSpan.Zero,
                BackoffMultiplier = 1.0
            });

        [Fact]
        public async Task GivenSuccessOnFirstAttempt_HandlerInvokedOnce_NothingParked() {
            var sink = A.Fake<IParkedMessageSink>();
            var callCount = 0;
            var policy = BuildPolicy(sink);

            await policy.ExecuteAsync(
                _ => { callCount++; return ValueTask.CompletedTask; },
                "sub-1", AnyMessage, TestContext.Current.CancellationToken);

            callCount.Should().Be(1);
            A.CallTo(() => sink.ParkAsync(A<string>._, A<SubscriptionMessage>._, A<Exception>._, A<CancellationToken>._))
                .MustNotHaveHappened();
        }

        [Fact]
        public async Task GivenTransientFailureThenSuccess_HandlerRetriedUntilSuccess_NothingParked() {
            var sink = A.Fake<IParkedMessageSink>();
            var callCount = 0;
            var policy = BuildPolicy(sink, maxRetries: 3);

            await policy.ExecuteAsync(
                _ => {
                    callCount++;
                    if (callCount < 3) throw new InvalidOperationException("transient");
                    return ValueTask.CompletedTask;
                },
                "sub-1", AnyMessage, TestContext.Current.CancellationToken);

            callCount.Should().Be(3);
            A.CallTo(() => sink.ParkAsync(A<string>._, A<SubscriptionMessage>._, A<Exception>._, A<CancellationToken>._))
                .MustNotHaveHappened();
        }

        [Fact]
        public async Task GivenAllRetriesExhausted_MessageParked_ReturnsNormally() {
            var sink = A.Fake<IParkedMessageSink>();
            var callCount = 0;
            var policy = BuildPolicy(sink, maxRetries: 3);

            // Should return normally (not throw) after parking.
            await policy.ExecuteAsync(
                _ => { callCount++; throw new InvalidOperationException("permanent"); },
                "sub-1", AnyMessage, TestContext.Current.CancellationToken);

            callCount.Should().Be(3, because: "all three attempts should be made before parking");
            A.CallTo(() => sink.ParkAsync(
                    "sub-1",
                    AnyMessage,
                    A<InvalidOperationException>._,
                    A<CancellationToken>._))
                .MustHaveHappenedOnceExactly();
        }

        [Fact]
        public async Task GivenMaxRetriesIsOne_ImmediatelyParkedOnFirstFailure_HandlerCalledOnce() {
            var sink = A.Fake<IParkedMessageSink>();
            var callCount = 0;
            var policy = BuildPolicy(sink, maxRetries: 1);

            await policy.ExecuteAsync(
                _ => { callCount++; throw new InvalidOperationException("boom"); },
                "sub-1", AnyMessage, TestContext.Current.CancellationToken);

            callCount.Should().Be(1);
            A.CallTo(() => sink.ParkAsync(A<string>._, A<SubscriptionMessage>._, A<Exception>._, A<CancellationToken>._))
                .MustHaveHappenedOnceExactly();
        }

        [Fact]
        public async Task GivenOperationCanceled_PropagatesWithoutParking() {
            var sink = A.Fake<IParkedMessageSink>();
            var policy = BuildPolicy(sink, maxRetries: 3);
            using var cts = new CancellationTokenSource();
            cts.Cancel();

            var act = () => policy.ExecuteAsync(
                ct => { ct.ThrowIfCancellationRequested(); return ValueTask.CompletedTask; },
                "sub-1", AnyMessage, cts.Token).AsTask();

            await act.Should().ThrowAsync<OperationCanceledException>();
            A.CallTo(() => sink.ParkAsync(A<string>._, A<SubscriptionMessage>._, A<Exception>._, A<CancellationToken>._))
                .MustNotHaveHappened();
        }

        [Fact]
        public async Task GivenPark_CorrectSubscriptionIdAndMessageForwarded() {
            var sink = A.Fake<IParkedMessageSink>();
            var policy = BuildPolicy(sink, maxRetries: 1);
            var message = new SubscriptionMessage(Event: null, CommitPosition: 99UL, Source: new EventReference("stream-1", 99), Metadata: EventMetadata.Empty);

            await policy.ExecuteAsync(
                _ => throw new InvalidOperationException(),
                "my-subscription", message, TestContext.Current.CancellationToken);

            A.CallTo(() => sink.ParkAsync(
                    "my-subscription",
                    message,
                    A<InvalidOperationException>._,
                    A<CancellationToken>._))
                .MustHaveHappenedOnceExactly();
        }
    }

    public class Block {
        static readonly SubscriptionMessage AnyMessage =
            new(Event: null, CommitPosition: 42UL, Source: new EventReference("stream-1", 42), Metadata: EventMetadata.Empty);

        static SubscriptionRetryPolicy BuildPolicy(IParkedMessageSink sink, int maxRetries, ILogger<SubscriptionRetryPolicy>? logger = null) =>
            new(sink, new SubscriptionErrorHandlingOptions {
                MaxRetries = maxRetries,
                InitialDelay = TimeSpan.Zero,
                MaxDelay = TimeSpan.Zero,
                BackoffMultiplier = 1.0
            }, logger);

        [Fact]
        public async Task Block_RetriesBeyondMaxRetries_UntilSuccess_WithoutParking() {
            var sink = A.Fake<IParkedMessageSink>();
            var callCount = 0;
            var policy = BuildPolicy(sink, maxRetries: 2);

            await policy.ExecuteAsync(
                _ => {
                    callCount++;
                    if (callCount < 6) throw new InvalidOperationException("still failing");
                    return ValueTask.CompletedTask;
                },
                "sub-1", AnyMessage, SubscriptionErrorMode.Block, TestContext.Current.CancellationToken);

            callCount.Should().Be(6);
            A.CallTo(() => sink.ParkAsync(A<string>._, A<SubscriptionMessage>._, A<Exception>._, A<CancellationToken>._))
                .MustNotHaveHappened();
        }

        [Fact]
        public async Task Block_LogsAnErrorOnce_WhenItStartsBlocking_AndInformationWhenItContinues() {
            var sink = A.Fake<IParkedMessageSink>();
            var logger = new FakeLogger<SubscriptionRetryPolicy>();
            var secondFailure = new InvalidOperationException("second failure");
            var callCount = 0;
            var policy = BuildPolicy(sink, maxRetries: 2, logger);

            await policy.ExecuteAsync(
                _ => {
                    callCount++;
                    if (callCount < 6) throw callCount == 2 ? secondFailure : new InvalidOperationException("still failing");
                    return ValueTask.CompletedTask;
                },
                "sub-1", AnyMessage, SubscriptionErrorMode.Block, TestContext.Current.CancellationToken);

            var records = logger.Collector.GetSnapshot();
            var error = records.Should().ContainSingle(record => record.Level == LogLevel.Error).Which;
            error.Message.Should().Contain("blocks and retries");
            error.Exception.Should().BeSameAs(secondFailure);
            var information = records.Should().ContainSingle(record => record.Level == LogLevel.Information).Which;
            information.Message.Should().Contain("no longer blocks");
            A.CallTo(() => sink.ParkAsync(A<string>._, A<SubscriptionMessage>._, A<Exception>._, A<CancellationToken>._))
                .MustNotHaveHappened();
        }

        [Fact]
        public async Task Block_StopsOnCancellation_WithoutParking() {
            var sink = A.Fake<IParkedMessageSink>();
            var policy = new SubscriptionRetryPolicy(sink, new SubscriptionErrorHandlingOptions {
                MaxRetries = 2,
                InitialDelay = TimeSpan.FromMilliseconds(1),
                MaxDelay = TimeSpan.FromMilliseconds(1)
            });
            using var cts = CancellationTokenSource.CreateLinkedTokenSource(TestContext.Current.CancellationToken);
            cts.CancelAfter(TimeSpan.FromMilliseconds(50));

            var act = () => policy.ExecuteAsync(
                _ => throw new InvalidOperationException("still failing"),
                "sub-1", AnyMessage, SubscriptionErrorMode.Block, cts.Token).AsTask();

            await act.Should().ThrowAsync<OperationCanceledException>();
            A.CallTo(() => sink.ParkAsync(A<string>._, A<SubscriptionMessage>._, A<Exception>._, A<CancellationToken>._))
                .MustNotHaveHappened();
        }

        [Fact]
        public async Task Park_IsTheDefault_OfTheOverloadWithoutMode() {
            var sink = A.Fake<IParkedMessageSink>();
            var callCount = 0;
            var policy = BuildPolicy(sink, maxRetries: 2);

            await policy.ExecuteAsync(
                _ => { callCount++; throw new InvalidOperationException("permanent"); },
                "sub-1", AnyMessage, SubscriptionErrorMode.Park, TestContext.Current.CancellationToken);

            callCount.Should().Be(2);
            A.CallTo(() => sink.ParkAsync(A<string>._, A<SubscriptionMessage>._, A<Exception>._, A<CancellationToken>._))
                .MustHaveHappenedOnceExactly();
        }
    }

    public class TryExecuteAsync {
        static SubscriptionRetryPolicy BuildPolicy(IParkedMessageSink sink, int maxRetries = 3) =>
            new(sink, new SubscriptionErrorHandlingOptions {
                MaxRetries = maxRetries,
                InitialDelay = TimeSpan.Zero,
                MaxDelay = TimeSpan.Zero,
                BackoffMultiplier = 1.0
            });

        [Fact]
        public async Task TryExecuteAsync_ReturnsNull_WhenAnAttemptSucceeds() {
            var sink = A.Fake<IParkedMessageSink>();
            var callCount = 0;
            var policy = BuildPolicy(sink, maxRetries: 3);

            var error = await policy.TryExecuteAsync(
                _ => {
                    callCount++;
                    if (callCount < 2) throw new InvalidOperationException("transient");
                    return ValueTask.CompletedTask;
                }, TestContext.Current.CancellationToken);

            error.Should().BeNull();
            callCount.Should().Be(2);
        }

        [Fact]
        public async Task TryExecuteAsync_ReturnsTheLastException_AfterMaxRetries_WithoutParking() {
            var sink = A.Fake<IParkedMessageSink>();
            var callCount = 0;
            var policy = BuildPolicy(sink, maxRetries: 3);
            var third = new InvalidOperationException("failure 3");

            var error = await policy.TryExecuteAsync(
                _ => {
                    callCount++;
                    if (callCount < 3) throw new InvalidOperationException($"failure {callCount}");
                    throw third;
                }, TestContext.Current.CancellationToken);

            callCount.Should().Be(3);
            error.Should().BeSameAs(third);
            A.CallTo(() => sink.ParkAsync(A<string>._, A<SubscriptionMessage>._, A<Exception>._, A<CancellationToken>._))
                .MustNotHaveHappened();
        }

        [Fact]
        public async Task TryExecuteAsync_PropagatesCancellation() {
            var sink = A.Fake<IParkedMessageSink>();
            var policy = BuildPolicy(sink, maxRetries: 3);
            using var cts = new CancellationTokenSource();
            cts.Cancel();

            var act = () => policy.TryExecuteAsync(
                ct => { ct.ThrowIfCancellationRequested(); return ValueTask.CompletedTask; },
                cts.Token).AsTask();

            await act.Should().ThrowAsync<OperationCanceledException>();
            A.CallTo(() => sink.ParkAsync(A<string>._, A<SubscriptionMessage>._, A<Exception>._, A<CancellationToken>._))
                .MustNotHaveHappened();
        }
    }
}
