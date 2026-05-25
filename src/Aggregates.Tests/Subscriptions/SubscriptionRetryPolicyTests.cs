using Aggregates.Subscriptions;
using FakeItEasy;
using FluentAssertions;

namespace Aggregates.Subscriptions;

public class SubscriptionRetryPolicyTests {

    public class ExecuteAsync {
        static readonly SubscriptionMessage AnyMessage =
            new(Event: null, CommitPosition: 42UL, Metadata: EventMetadata.Empty);

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
            var message = new SubscriptionMessage(Event: null, CommitPosition: 99UL, Metadata: EventMetadata.Empty);

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
}
