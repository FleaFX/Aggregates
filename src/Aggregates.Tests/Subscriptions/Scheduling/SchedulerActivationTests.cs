using Aggregates.Sagas;
using FakeItEasy;
using FluentAssertions;

namespace Aggregates.Subscriptions;

public class SchedulerActivationTests {
    const string Own = "Ns.Saga@v2";
    const string Predecessor = "Ns.Saga@v1";

    static readonly byte[] Activated = "activated"u8.ToArray();

    static readonly SubscriptionRegistration WithPredecessor =
        new(Own, typeof(DeferredCommandTestSaga), typeof(TestEvent), false, [Predecessor]);

    [Fact]
    public async Task WithoutMarkerStreams_IsNotActivated() {
        var store = A.Fake<IScheduledStore>();
        var activation = new SchedulerActivation<DeferredCommandTestSaga>(store, [WithPredecessor]);

        var activated = await activation.IsActivatedAsync(WithPredecessor, TestContext.Current.CancellationToken);

        activated.Should().BeFalse();
        activation.Activated.IsCompleted.Should().BeFalse();
    }

    [Fact]
    public async Task WithItsMarkerStream_IsActivated() {
        var store = A.Fake<IScheduledStore>();
        A.CallTo(() => store.ExistsAsync(ScheduledFormat.SchedulerStream(Own), A<CancellationToken>._)).Returns(true);
        var activation = new SchedulerActivation<DeferredCommandTestSaga>(store, [WithPredecessor]);

        var activated = await activation.IsActivatedAsync(WithPredecessor, TestContext.Current.CancellationToken);

        activated.Should().BeTrue();
        activation.Activated.IsCompleted.Should().BeTrue();
    }

    [Fact]
    public async Task WithAPredecessorsMarkerStream_IsActivated() {
        var store = A.Fake<IScheduledStore>();
        A.CallTo(() => store.ExistsAsync(ScheduledFormat.SchedulerStream(Predecessor), A<CancellationToken>._)).Returns(true);
        var activation = new SchedulerActivation<DeferredCommandTestSaga>(store, [WithPredecessor]);

        var activated = await activation.IsActivatedAsync(WithPredecessor, TestContext.Current.CancellationToken);

        activated.Should().BeTrue();
        activation.Activated.IsCompleted.Should().BeTrue();
    }

    [Fact]
    public async Task EnsureActivated_WritesTheMarkerOnce_AndCompletes() {
        var store = A.Fake<IScheduledStore>();
        var activation = new SchedulerActivation<DeferredCommandTestSaga>(store, [WithPredecessor]);

        await activation.EnsureActivatedAsync(TestContext.Current.CancellationToken);
        await activation.EnsureActivatedAsync(TestContext.Current.CancellationToken);

        A.CallTo(() => store.AppendAsync(ScheduledFormat.SchedulerStream(Own), ScheduledFormat.MarkerEventType,
                A<ReadOnlyMemory<byte>>.That.Matches(data => data.ToArray().SequenceEqual(Activated)), A<CancellationToken>._))
            .MustHaveHappenedOnceExactly();
        activation.Activated.IsCompleted.Should().BeTrue();
    }

    [Fact]
    public async Task EnsureActivated_WhenTheStreamExists_WritesNothing() {
        var store = A.Fake<IScheduledStore>();
        A.CallTo(() => store.ExistsAsync(ScheduledFormat.SchedulerStream(Own), A<CancellationToken>._)).Returns(true);
        var activation = new SchedulerActivation<DeferredCommandTestSaga>(store, [WithPredecessor]);

        await activation.EnsureActivatedAsync(TestContext.Current.CancellationToken);

        A.CallTo(() => store.AppendAsync(A<string>._, A<string>._, A<ReadOnlyMemory<byte>>._, A<CancellationToken>._))
            .MustNotHaveHappened();
        activation.Activated.IsCompleted.Should().BeTrue();
    }

    [Fact]
    public async Task EnsureActivated_WithoutRegistration_Throws() {
        var store = A.Fake<IScheduledStore>();
        var activation = new SchedulerActivation<DeferredCommandTestSaga>(store, []);

        var act = () => activation.EnsureActivatedAsync(TestContext.Current.CancellationToken).AsTask();

        await act.Should().ThrowAsync<InvalidOperationException>()
            .WithMessage("*has no subscription*");
    }
}
