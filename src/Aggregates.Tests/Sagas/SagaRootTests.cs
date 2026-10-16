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
    public async Task AcceptAsync_CallsReactAsyncWithStateBeforeApply() {
        // ReactAsync receives the state BEFORE Apply; the root's state includes the event afterwards.
        TestSagaState? capturedState = null;
        var saga = A.Fake<ISaga<TestSagaState, TestEvent>>();
        A.CallTo(() => saga.ReactAsync(A<TestSagaState>._, A<TestEvent>._, A<CancellationToken>._))
            .Invokes(call => capturedState = call.GetArgument<TestSagaState>(0))
            .Returns(AsyncEnumerable.Empty<ICommand>());
        var root = new SagaRoot<TestSagaState, TestEvent>(default, AggregateVersion.None);

        await root.AcceptAsync(new TestEvent(7), new EventReference("stream-1", 0), saga, TestContext.Current.CancellationToken);

        capturedState.Should().Be(new TestSagaState(0));
        root.State.Should().Be(new TestSagaState(7));
    }

    [Fact]
    public async Task AcceptAsync_InOneUnitOfWork_ReactsWithTheEarlierEventsApplied() {
        var capturedStates = new List<TestSagaState?>();
        var saga = A.Fake<ISaga<TestSagaState, TestEvent>>();
        A.CallTo(() => saga.ReactAsync(A<TestSagaState>._, A<TestEvent>._, A<CancellationToken>._))
            .Invokes(call => capturedStates.Add(call.GetArgument<TestSagaState>(0)))
            .Returns(AsyncEnumerable.Empty<ICommand>());
        var root = new SagaRoot<TestSagaState, TestEvent>(default, AggregateVersion.None);

        await root.AcceptAsync(new TestEvent(3), new EventReference("stream-1", 0), saga, TestContext.Current.CancellationToken);
        await root.AcceptAsync(new TestEvent(4), new EventReference("stream-1", 1), saga, TestContext.Current.CancellationToken);

        capturedStates.Should().Equal(new TestSagaState(0), new TestSagaState(3));
        root.State.Should().Be(new TestSagaState(7));
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

    // A saga that also receives deadlines; its reaction to events and to deadlines are set per test.
    static ISaga<TestSagaState, TestEvent, TestDeadline> DeadlineSaga(IAsyncEnumerable<ICommand> reaction) {
        var saga = A.Fake<ISaga<TestSagaState, TestEvent, TestDeadline>>();
        A.CallTo(() => saga.ReactAsync(A<TestSagaState>._, A<TestEvent>._, A<CancellationToken>._))
            .Returns(reaction);
        A.CallTo(() => saga.ReactToDeadlineAsync(A<TestSagaState>._, A<TestDeadline>._, A<CancellationToken>._))
            .Returns(AsyncEnumerable.Empty<ICommand>());
        return saga;
    }

    [Fact]
    public async Task AcceptAsync_RecordsARequestedDeadline_AndDoesNotReturnIt() {
        var due = new DateTimeOffset(2026, 11, 1, 0, 0, 0, TimeSpan.Zero);
        var saga = DeadlineSaga(new ICommand[] { Deadline.At(due, new TestDeadline(1)), new TestCommand() }.ToAsyncEnumerable());
        var root = new SagaRoot<TestSagaState, TestEvent>(default, AggregateVersion.None);
        var source = new EventReference("stream-1", 3);

        var commands = await root.AcceptAsync(new TestEvent(1), source, saga, TestContext.Current.CancellationToken);

        var owner = SubscriptionRegistrations.GetSubscriptionId(saga.GetType(), null);
        root.GetChanges().Should().Equal(source, new DeadlineRequest(owner, due, new TestDeadline(1)));
        commands.Should().ContainSingle().Which.Should().BeOfType<TestCommand>();
    }

    [Fact]
    public async Task AcceptAsync_ADeadlineWithAPayloadOfAnotherType_Throws() {
        var saga = DeadlineSaga(new ICommand[] { Deadline.At(DateTimeOffset.UtcNow, "text") }.ToAsyncEnumerable());
        var root = new SagaRoot<TestSagaState, TestEvent>(default, AggregateVersion.None);

        var act = () => root.AcceptAsync(new TestEvent(1), new EventReference("stream-1", 0), saga, TestContext.Current.CancellationToken);

        await act.Should().ThrowAsync<InvalidOperationException>()
            .WithMessage("*receives deadlines of type TestDeadline*");
    }

    [Fact]
    public async Task AcceptAsync_ADeadlineFromASagaWithoutDeadlines_Throws() {
        var saga = A.Fake<ISaga<TestSagaState, TestEvent>>();
        A.CallTo(() => saga.ReactAsync(A<TestSagaState>._, A<TestEvent>._, A<CancellationToken>._))
            .Returns(new ICommand[] { Deadline.At(DateTimeOffset.UtcNow, new TestDeadline(1)) }.ToAsyncEnumerable());
        var root = new SagaRoot<TestSagaState, TestEvent>(default, AggregateVersion.None);

        var act = () => root.AcceptAsync(new TestEvent(1), new EventReference("stream-1", 0), saga, TestContext.Current.CancellationToken);

        await act.Should().ThrowAsync<InvalidOperationException>()
            .WithMessage("*doesn't implement ISaga<TSagaState, TEvent, TDeadline>*");
    }

    [Fact]
    public async Task AcceptAsync_ADeferredDeadline_Throws() {
        var saga = DeadlineSaga(new ICommand[] { Deadline.At(DateTimeOffset.UtcNow, new TestDeadline(1)).DeferUntil(DateTimeOffset.UtcNow) }.ToAsyncEnumerable());
        var root = new SagaRoot<TestSagaState, TestEvent>(default, AggregateVersion.None);

        var act = () => root.AcceptAsync(new TestEvent(1), new EventReference("stream-1", 0), saga, TestContext.Current.CancellationToken);

        await act.Should().ThrowAsync<InvalidOperationException>()
            .WithMessage("*defers a deadline*");
    }

    [Fact]
    public async Task AcceptDeadlineAsync_ReactsWithTheCurrentState_AndChangesNothing() {
        var command = new TestCommand();
        var saga = A.Fake<ISaga<TestSagaState, TestEvent, TestDeadline>>();
        TestSagaState? capturedState = null;
        TestDeadline? capturedDeadline = null;
        A.CallTo(() => saga.ReactToDeadlineAsync(A<TestSagaState>._, A<TestDeadline>._, A<CancellationToken>._))
            .Invokes(call => {
                capturedState = call.GetArgument<TestSagaState>(0);
                capturedDeadline = call.GetArgument<TestDeadline>(1);
            })
            .Returns(new ICommand[] { command }.ToAsyncEnumerable());
        var root = new SagaRoot<TestSagaState, TestEvent>(new TestSagaState(5), AggregateVersion.None);
        var request = new EventReference("saga-1", 2);

        var commands = await root.AcceptDeadlineAsync(new TestDeadline(3), request, saga, TestContext.Current.CancellationToken);

        capturedState.Should().Be(new TestSagaState(5));
        capturedDeadline.Should().Be(new TestDeadline(3));
        root.State.Should().Be(new TestSagaState(5));
        root.GetChanges().Should().BeEmpty();
        commands.Should().ContainSingle().Which.Should().Be(command);
    }

    [Fact]
    public async Task AcceptDeadlineAsync_RecordsItsRequests_WithTheDeadlineAsCause() {
        var due = new DateTimeOffset(2026, 11, 1, 0, 0, 0, TimeSpan.Zero);
        var saga = A.Fake<ISaga<TestSagaState, TestEvent, TestDeadline>>();
        A.CallTo(() => saga.ReactToDeadlineAsync(A<TestSagaState>._, A<TestDeadline>._, A<CancellationToken>._))
            .Returns(new ICommand[] { new TestCommand().DeferUntil(due), Deadline.At(due, new TestDeadline(1)) }.ToAsyncEnumerable());
        var root = new SagaRoot<TestSagaState, TestEvent>(default, AggregateVersion.None);
        var request = new EventReference("saga-1", 2);

        await root.AcceptDeadlineAsync(new TestDeadline(3), request, saga, TestContext.Current.CancellationToken);

        var owner = SubscriptionRegistrations.GetSubscriptionId(saga.GetType(), null);
        root.GetChanges().Should().Equal(
            new DeferredCommandRequest(owner, due, new TestCommand()) { Cause = request },
            new DeadlineRequest(owner, due, new TestDeadline(1)) { Cause = request });
    }

    [Fact]
    public async Task AcceptDeadlineAsync_ForADeadlineWhoseRequestsAreStored_DoesNotReact() {
        // A deadline delivered again after its reaction stored requests must not store them twice.
        var saga = A.Fake<ISaga<TestSagaState, TestEvent, TestDeadline>>();
        A.CallTo(() => saga.ReactToDeadlineAsync(A<TestSagaState>._, A<TestDeadline>._, A<CancellationToken>._))
            .Returns(new ICommand[] { new TestCommand() }.ToAsyncEnumerable());
        var request = new EventReference("saga-1", 2);
        var root = new SagaRoot<TestSagaState, TestEvent>(default, AggregateVersion.None, new HashSet<EventReference> { request });

        var commands = await root.AcceptDeadlineAsync(new TestDeadline(3), request, saga, TestContext.Current.CancellationToken);

        commands.Should().BeEmpty();
        root.GetChanges().Should().BeEmpty();
        A.CallTo(() => saga.ReactToDeadlineAsync(A<TestSagaState>._, A<TestDeadline>._, A<CancellationToken>._)).MustNotHaveHappened();
    }
}
