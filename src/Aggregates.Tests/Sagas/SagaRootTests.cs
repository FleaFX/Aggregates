using FakeItEasy;
using FluentAssertions;

namespace Aggregates.Sagas;

public class SagaRootTests {
    [Fact]
    public async Task AcceptAsync_AppliesEventToState() {
        var saga = A.Fake<ISaga<TestSagaState, TestEvent>>();
        A.CallTo(() => saga.ReactAsync(A<TestSagaState>._, A<TestEvent>._, A<CancellationToken>._))
            .Returns(AsyncEnumerable.Empty<ICommand>());
        var root = new SagaRoot<TestSagaState, TestEvent>(AggregateVersion.None);

        await root.AcceptAsync(new TestEvent(5), saga, TestContext.Current.CancellationToken);

        root.State.Total.Should().Be(5);
    }

    [Fact]
    public async Task AcceptAsync_AddsEventToChanges() {
        var saga = A.Fake<ISaga<TestSagaState, TestEvent>>();
        A.CallTo(() => saga.ReactAsync(A<TestSagaState>._, A<TestEvent>._, A<CancellationToken>._))
            .Returns(AsyncEnumerable.Empty<ICommand>());
        var root = new SagaRoot<TestSagaState, TestEvent>(AggregateVersion.None);
        var @event = new TestEvent(1);

        await root.AcceptAsync(@event, saga, TestContext.Current.CancellationToken);

        root.GetChanges().Should().ContainSingle().Which.Should().Be(@event);
    }

    [Fact]
    public async Task AcceptAsync_CallsReactAsyncWithStateAfterApply() {
        // ReactAsync must receive the state AFTER Apply, not the state before.
        TestSagaState? capturedState = null;
        var saga = A.Fake<ISaga<TestSagaState, TestEvent>>();
        A.CallTo(() => saga.ReactAsync(A<TestSagaState>._, A<TestEvent>._, A<CancellationToken>._))
            .Invokes(call => capturedState = call.GetArgument<TestSagaState>(0))
            .Returns(AsyncEnumerable.Empty<ICommand>());
        var root = new SagaRoot<TestSagaState, TestEvent>(AggregateVersion.None);

        await root.AcceptAsync(new TestEvent(7), saga, TestContext.Current.CancellationToken);

        capturedState.Should().Be(new TestSagaState(7));
    }

    [Fact]
    public async Task AcceptAsync_ReturnsCommandsFromReactAsync() {
        var command = new TestCommand();
        var saga = A.Fake<ISaga<TestSagaState, TestEvent>>();
        A.CallTo(() => saga.ReactAsync(A<TestSagaState>._, A<TestEvent>._, A<CancellationToken>._))
            .Returns(new ICommand[] { command }.ToAsyncEnumerable());
        var root = new SagaRoot<TestSagaState, TestEvent>(AggregateVersion.None);

        var commands = await root.AcceptAsync(new TestEvent(1), saga, TestContext.Current.CancellationToken);

        commands.Should().ContainSingle().Which.Should().Be(command);
    }

    [Fact]
    public async Task AcceptAsync_MultipleCalls_AccumulatesChanges() {
        var saga = A.Fake<ISaga<TestSagaState, TestEvent>>();
        A.CallTo(() => saga.ReactAsync(A<TestSagaState>._, A<TestEvent>._, A<CancellationToken>._))
            .Returns(AsyncEnumerable.Empty<ICommand>());
        var root = new SagaRoot<TestSagaState, TestEvent>(AggregateVersion.None);

        await root.AcceptAsync(new TestEvent(3), saga, TestContext.Current.CancellationToken);
        await root.AcceptAsync(new TestEvent(4), saga, TestContext.Current.CancellationToken);

        root.State.Total.Should().Be(7);
        root.GetChanges().Should().HaveCount(2);
    }
}
