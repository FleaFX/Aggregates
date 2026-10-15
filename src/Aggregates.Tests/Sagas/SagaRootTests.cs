using Aggregates.Subscriptions;
using FakeItEasy;
using FluentAssertions;

namespace Aggregates.Sagas;

public class SagaRootTests {
    static ISaga<TestSagaState, TestEvent> SilentSaga() {
        var saga = A.Fake<ISaga<TestSagaState, TestEvent>>();
        A.CallTo(() => saga.ReactAsync(A<TestSagaState>._, A<TestEvent>._, A<CancellationToken>._))
            .Returns(AsyncEnumerable.Empty<ICommand>());
        return saga;
    }

    [Fact]
    public async Task AcceptAsync_AppliesEventToState() {
        var root = new SagaRoot<TestSagaState, TestEvent>(default, AggregateVersion.None);

        await root.AcceptAsync(new TestEvent(5), new EventReference("stream-1", 0), SilentSaga(), TestContext.Current.CancellationToken);

        root.State.Total.Should().Be(5);
    }

    [Fact]
    public async Task AcceptAsync_AddsSourceToChanges() {
        var root = new SagaRoot<TestSagaState, TestEvent>(default, AggregateVersion.None);
        var source = new EventReference("stream-1", 4);

        await root.AcceptAsync(new TestEvent(1), source, SilentSaga(), TestContext.Current.CancellationToken);

        root.GetChanges().Should().ContainSingle().Which.Should().Be(source);
    }

    [Fact]
    public async Task AcceptAsync_CallsReactAsyncWithStateAfterApply() {
        // ReactAsync must receive the state AFTER Apply, not the state before.
        TestSagaState? capturedState = null;
        var saga = A.Fake<ISaga<TestSagaState, TestEvent>>();
        A.CallTo(() => saga.ReactAsync(A<TestSagaState>._, A<TestEvent>._, A<CancellationToken>._))
            .Invokes(call => capturedState = call.GetArgument<TestSagaState>(0))
            .Returns(AsyncEnumerable.Empty<ICommand>());
        var root = new SagaRoot<TestSagaState, TestEvent>(default, AggregateVersion.None);

        await root.AcceptAsync(new TestEvent(7), new EventReference("stream-1", 0), saga, TestContext.Current.CancellationToken);

        capturedState.Should().Be(new TestSagaState(7));
    }

    [Fact]
    public async Task AcceptAsync_ReturnsCommandsFromReactAsync() {
        var command = new TestCommand();
        var saga = A.Fake<ISaga<TestSagaState, TestEvent>>();
        A.CallTo(() => saga.ReactAsync(A<TestSagaState>._, A<TestEvent>._, A<CancellationToken>._))
            .Returns(new ICommand[] { command }.ToAsyncEnumerable());
        var root = new SagaRoot<TestSagaState, TestEvent>(default, AggregateVersion.None);

        var commands = await root.AcceptAsync(new TestEvent(1), new EventReference("stream-1", 0), saga, TestContext.Current.CancellationToken);

        commands.Should().ContainSingle().Which.Should().Be(command);
    }

    [Fact]
    public async Task AcceptAsync_MultipleCalls_AccumulatesChanges() {
        var root = new SagaRoot<TestSagaState, TestEvent>(default, AggregateVersion.None);

        await root.AcceptAsync(new TestEvent(3), new EventReference("stream-1", 0), SilentSaga(), TestContext.Current.CancellationToken);
        await root.AcceptAsync(new TestEvent(4), new EventReference("stream-1", 1), SilentSaga(), TestContext.Current.CancellationToken);

        root.State.Total.Should().Be(7);
        root.GetChanges().Should().HaveCount(2);
    }

    [Fact]
    public async Task AcceptAsync_WithSourceAlreadyInChanges_DoesNotReactAgain() {
        // The same source twice in one unit of work: one link, one reaction.
        var saga = A.Fake<ISaga<TestSagaState, TestEvent>>();
        A.CallTo(() => saga.ReactAsync(A<TestSagaState>._, A<TestEvent>._, A<CancellationToken>._))
            .Returns(AsyncEnumerable.Empty<ICommand>());
        var root = new SagaRoot<TestSagaState, TestEvent>(default, AggregateVersion.None);
        var source = new EventReference("stream-1", 0);

        var first = await root.AcceptAsync(new TestEvent(5), source, saga, TestContext.Current.CancellationToken);
        var second = await root.AcceptAsync(new TestEvent(6), source, saga, TestContext.Current.CancellationToken);

        second.Should().BeEmpty();
        root.State.Total.Should().Be(5);
        root.GetChanges().Should().ContainSingle();
        A.CallTo(() => saga.ReactAsync(A<TestSagaState>._, A<TestEvent>._, A<CancellationToken>._)).MustHaveHappenedOnceExactly();
    }

    [Fact]
    public async Task AcceptAsync_WithSourceAlreadyHandled_DoesNotReact() {
        // A source the saga stream already links to: no change, no Apply, no reaction.
        var saga = A.Fake<ISaga<TestSagaState, TestEvent>>();
        A.CallTo(() => saga.ReactAsync(A<TestSagaState>._, A<TestEvent>._, A<CancellationToken>._))
            .Returns(AsyncEnumerable.Empty<ICommand>());
        var source = new EventReference("stream-1", 0);
        var root = new SagaRoot<TestSagaState, TestEvent>(default, AggregateVersion.None, new HashSet<EventReference> { source });

        var commands = await root.AcceptAsync(new TestEvent(5), source, saga, TestContext.Current.CancellationToken);

        commands.Should().BeEmpty();
        root.State.Total.Should().Be(0);
        root.GetChanges().Should().BeEmpty();
        A.CallTo(() => saga.ReactAsync(A<TestSagaState>._, A<TestEvent>._, A<CancellationToken>._)).MustNotHaveHappened();
    }

    [Fact]
    public async Task AcceptAsync_RecordsADeferredCommand_AndDoesNotReturnItAsACommand() {
        var due = new DateTimeOffset(2026, 11, 1, 0, 0, 0, TimeSpan.Zero);
        var saga = A.Fake<ISaga<TestSagaState, TestEvent>>();
        A.CallTo(() => saga.ReactAsync(A<TestSagaState>._, A<TestEvent>._, A<CancellationToken>._))
            .Returns(new ICommand[] { new TestCommand().DeferUntil(due), new TestCommand() }.ToAsyncEnumerable());
        var root = new SagaRoot<TestSagaState, TestEvent>(default, AggregateVersion.None);
        var source = new EventReference("stream-1", 3);

        var commands = await root.AcceptAsync(new TestEvent(1), source, saga, TestContext.Current.CancellationToken);

        var owner = SubscriptionRegistrations.GetSubscriptionId(saga.GetType(), null);
        root.GetChanges().Should().Equal(source, new DeferredCommandRequest(owner, due, new TestCommand()));
        commands.Should().ContainSingle().Which.Should().BeOfType<TestCommand>();
    }

    [Fact]
    public async Task AcceptAsync_TheOwnerIsTheSubscriptionIdOfTheSaga() {
        var root = new SagaRoot<TestSagaState, TestEvent>(default, AggregateVersion.None);

        await root.AcceptAsync(new TestEvent(1), new EventReference("stream-1", 0), new DeferredCommandTestSaga(), TestContext.Current.CancellationToken);

        root.GetChanges().OfType<DeferredCommandRequest>().Should().ContainSingle()
            .Which.Owner.Should().Be("Tests.DeferredCommandTest@v1");
    }

    [Fact]
    public async Task AcceptAsync_ForAHandledSource_RecordsNoDeferredCommand() {
        var source = new EventReference("stream-1", 0);
        var saga = A.Fake<ISaga<TestSagaState, TestEvent>>();
        A.CallTo(() => saga.ReactAsync(A<TestSagaState>._, A<TestEvent>._, A<CancellationToken>._))
            .Returns(new ICommand[] { new TestCommand().DeferUntil(DateTimeOffset.UtcNow) }.ToAsyncEnumerable());
        var root = new SagaRoot<TestSagaState, TestEvent>(default, AggregateVersion.None, new HashSet<EventReference> { source });

        var commands = await root.AcceptAsync(new TestEvent(1), source, saga, TestContext.Current.CancellationToken);

        root.GetChanges().Should().BeEmpty();
        commands.Should().BeEmpty();
        A.CallTo(() => saga.ReactAsync(A<TestSagaState>._, A<TestEvent>._, A<CancellationToken>._)).MustNotHaveHappened();
    }
}
