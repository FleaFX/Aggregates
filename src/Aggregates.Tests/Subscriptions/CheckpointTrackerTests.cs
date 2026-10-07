using FakeItEasy;
using FluentAssertions;
using Microsoft.Extensions.Time.Testing;

namespace Aggregates.Subscriptions;

public class CheckpointTrackerTests {
    const string SubscriptionId = "sub-1";

    readonly ICheckpointStore _store = A.Fake<ICheckpointStore>();
    readonly FakeTimeProvider _time = new();

    CheckpointTracker BuildTracker(int maxBatchSize = 3, int maxIntervalSeconds = 5) =>
        new(_store, SubscriptionId, new SubscriptionCheckpointOptions {
            MaxBatchSize = maxBatchSize,
            MaxInterval = TimeSpan.FromSeconds(maxIntervalSeconds)
        }, _time);

    static CancellationToken Token => TestContext.Current.CancellationToken;

    public class AdvanceAsync : CheckpointTrackerTests {
        [Fact]
        public async Task GivenBothThresholdsNotReached_StoresNothing() {
            var tracker = BuildTracker();

            await tracker.AdvanceAsync(1, Token);
            _time.Advance(TimeSpan.FromSeconds(4));
            await tracker.AdvanceAsync(2, Token);

            A.CallTo(() => _store.StoreAsync(A<string>._, A<ulong>._, A<CancellationToken>._)).MustNotHaveHappened();
        }

        [Fact]
        public async Task GivenMaxBatchSizeReached_StoresLastPosition() {
            var tracker = BuildTracker(maxBatchSize: 3);

            await tracker.AdvanceAsync(1, Token);
            await tracker.AdvanceAsync(2, Token);
            await tracker.AdvanceAsync(3, Token);

            A.CallTo(() => _store.StoreAsync(A<string>._, A<ulong>._, A<CancellationToken>._)).MustHaveHappenedOnceExactly();
            A.CallTo(() => _store.StoreAsync(SubscriptionId, 3UL, A<CancellationToken>._)).MustHaveHappenedOnceExactly();
        }

        [Fact]
        public async Task GivenMaxBatchSizeReachedTwice_StoresOncePerBatch() {
            var tracker = BuildTracker(maxBatchSize: 3);

            for (ulong position = 1; position <= 7; position++)
                await tracker.AdvanceAsync(position, Token);

            A.CallTo(() => _store.StoreAsync(SubscriptionId, 3UL, A<CancellationToken>._)).MustHaveHappenedOnceExactly()
                .Then(A.CallTo(() => _store.StoreAsync(SubscriptionId, 6UL, A<CancellationToken>._)).MustHaveHappenedOnceExactly());
            A.CallTo(() => _store.StoreAsync(A<string>._, A<ulong>._, A<CancellationToken>._)).MustHaveHappenedTwiceExactly();
        }

        [Fact]
        public async Task GivenMaxIntervalElapsed_StoresOnNextMessage() {
            var tracker = BuildTracker(maxIntervalSeconds: 5);

            await tracker.AdvanceAsync(1, Token);
            _time.Advance(TimeSpan.FromSeconds(5));

            A.CallTo(() => _store.StoreAsync(A<string>._, A<ulong>._, A<CancellationToken>._)).MustNotHaveHappened();

            await tracker.AdvanceAsync(2, Token);

            A.CallTo(() => _store.StoreAsync(A<string>._, A<ulong>._, A<CancellationToken>._)).MustHaveHappenedOnceExactly();
            A.CallTo(() => _store.StoreAsync(SubscriptionId, 2UL, A<CancellationToken>._)).MustHaveHappenedOnceExactly();
        }

        [Fact]
        public async Task GivenMaxIntervalElapsedBeforeFirstMessage_DoesNotStore() {
            var tracker = BuildTracker(maxIntervalSeconds: 5);

            _time.Advance(TimeSpan.FromSeconds(10));
            await tracker.AdvanceAsync(1, Token);

            A.CallTo(() => _store.StoreAsync(A<string>._, A<ulong>._, A<CancellationToken>._)).MustNotHaveHappened();
        }

        [Fact]
        public async Task GivenFailedStore_RetriesOnNextMessage() {
            var tracker = BuildTracker(maxBatchSize: 1);
            A.CallTo(() => _store.StoreAsync(SubscriptionId, 1UL, A<CancellationToken>._))
                .ThrowsAsync(new InvalidOperationException("store unavailable"));

            var act = async () => await tracker.AdvanceAsync(1, Token);
            await act.Should().ThrowAsync<InvalidOperationException>();
            await tracker.AdvanceAsync(2, Token);

            A.CallTo(() => _store.StoreAsync(SubscriptionId, 2UL, A<CancellationToken>._)).MustHaveHappenedOnceExactly();
        }
    }

    public class FlushAsync : CheckpointTrackerTests {
        [Fact]
        public async Task GivenNoPendingPosition_StoresNothing() {
            var tracker = BuildTracker();

            await tracker.FlushAsync(Token);

            A.CallTo(() => _store.StoreAsync(A<string>._, A<ulong>._, A<CancellationToken>._)).MustNotHaveHappened();
        }

        [Fact]
        public async Task GivenPendingPosition_StoresItOnce() {
            var tracker = BuildTracker();
            await tracker.AdvanceAsync(1, Token);
            await tracker.AdvanceAsync(2, Token);

            await tracker.FlushAsync(Token);
            await tracker.FlushAsync(Token);

            A.CallTo(() => _store.StoreAsync(A<string>._, A<ulong>._, A<CancellationToken>._)).MustHaveHappenedOnceExactly();
            A.CallTo(() => _store.StoreAsync(SubscriptionId, 2UL, A<CancellationToken>._)).MustHaveHappenedOnceExactly();
        }

        [Fact]
        public async Task GivenPositionAlreadyStoredByBatch_StoresNothing() {
            var tracker = BuildTracker(maxBatchSize: 2);
            await tracker.AdvanceAsync(1, Token);
            await tracker.AdvanceAsync(2, Token);

            await tracker.FlushAsync(Token);

            A.CallTo(() => _store.StoreAsync(A<string>._, A<ulong>._, A<CancellationToken>._)).MustHaveHappenedOnceExactly();
        }

        [Fact]
        public async Task ResetsBatch_NextBatchStartsCountingFromZero() {
            var tracker = BuildTracker(maxBatchSize: 3);
            await tracker.AdvanceAsync(1, Token);
            await tracker.AdvanceAsync(2, Token);
            await tracker.FlushAsync(Token);

            await tracker.AdvanceAsync(3, Token);
            await tracker.AdvanceAsync(4, Token);

            A.CallTo(() => _store.StoreAsync(A<string>._, A<ulong>._, A<CancellationToken>._)).MustHaveHappenedOnceExactly();
        }
    }

    public class LastPosition : CheckpointTrackerTests {
        [Fact]
        public void GivenNoMessage_IsNull() {
            BuildTracker().LastPosition.Should().BeNull();
        }

        [Fact]
        public async Task GivenUnwrittenPositions_IsLastRecordedPosition() {
            var tracker = BuildTracker(maxBatchSize: 3);

            await tracker.AdvanceAsync(1, Token);
            await tracker.AdvanceAsync(2, Token);

            tracker.LastPosition.Should().Be(2UL);
            A.CallTo(() => _store.StoreAsync(A<string>._, A<ulong>._, A<CancellationToken>._)).MustNotHaveHappened();
        }

        [Fact]
        public async Task GivenWrittenPosition_IsKept() {
            var tracker = BuildTracker();
            await tracker.AdvanceAsync(1, Token);

            await tracker.FlushAsync(Token);

            tracker.LastPosition.Should().Be(1UL);
        }

        [Fact]
        public async Task GivenFailedWrite_IsKept() {
            var tracker = BuildTracker(maxBatchSize: 1);
            A.CallTo(() => _store.StoreAsync(SubscriptionId, 1UL, A<CancellationToken>._))
                .ThrowsAsync(new InvalidOperationException("store unavailable"));

            var act = async () => await tracker.AdvanceAsync(1, Token);
            await act.Should().ThrowAsync<InvalidOperationException>();

            tracker.LastPosition.Should().Be(1UL);
        }
    }

    public class Constructor : CheckpointTrackerTests {
        [Fact]
        public void GivenMaxBatchSizeBelowOne_Throws() {
            var act = () => BuildTracker(maxBatchSize: 0);

            act.Should().Throw<ArgumentOutOfRangeException>();
        }

        [Fact]
        public void GivenNegativeMaxInterval_Throws() {
            var act = () => BuildTracker(maxIntervalSeconds: -1);

            act.Should().Throw<ArgumentOutOfRangeException>();
        }
    }
}
