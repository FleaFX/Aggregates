using System.Runtime.CompilerServices;
using FluentAssertions;

namespace Aggregates;

public class EntityRootMetadataCollectionTests {
    // --- Domain types used across tests ---

    [StateValueMetadata("stateKey")]
    record struct TestState(string Value) : IState<TestState, TestEvent> {
        public static TestState Initial => new("initial");
        public TestState Apply(TestEvent @event) => new(@event.Value);
    }

    record TestEvent(string Value);

    [FixedMetadata("commandKey", "command-processed")]
    record TestCommand(AggregateIdentifier Id, string NewValue) : ICommand<TestState, TestEvent> {
        public async IAsyncEnumerable<TestEvent> ProgressAsync(
            TestState state,
            [EnumeratorCancellation] CancellationToken cancellationToken = default) {
            yield return new TestEvent(NewValue);
        }
    }

    /// <summary>
    /// Captures the current state value as metadata.
    /// </summary>
    sealed class StateValueMetadataAttribute(string key) : MetadataAttribute(key) {
        public override ValueTask<object?> GetValueAsync(object context, CancellationToken cancellationToken) =>
            ValueTask.FromResult<object?>(((TestState)context).Value);
    }

    /// <summary>
    /// Records a fixed string value as metadata.
    /// </summary>
    sealed class FixedMetadataAttribute(string key, string value) : MetadataAttribute(key) {
        public override ValueTask<object?> GetValueAsync(object context, CancellationToken cancellationToken) =>
            ValueTask.FromResult<object?>(value);
    }

    // --- Tests ---

    [Fact]
    public async Task GivenAttributeOnCommand_CollectsCommandMetadata() {
        await using var scope = new MetadataScope();
        var root = new EntityRoot<TestState, TestEvent>(default, AggregateVersion.None);

        await root.AcceptAsync(new TestCommand("agg/1", "new-value"), TestContext.Current.CancellationToken);

        scope.Snapshot()["commandKey"].Should().Be("command-processed");
    }

    [Fact]
    public async Task GivenAttributeOnState_CollectsStateMetadataAfterApply() {
        await using var scope = new MetadataScope();
        var root = new EntityRoot<TestState, TestEvent>(default, AggregateVersion.None);

        await root.AcceptAsync(new TestCommand("agg/1", "new-value"), TestContext.Current.CancellationToken);

        // The state attribute captures the value AFTER Apply, so it should be "new-value",
        // not the initial "initial".
        scope.Snapshot()["stateKey"].Should().Be("new-value");
    }

    [Fact]
    public async Task GivenMultipleEvents_StateMetadataReflectsLastAppliedState() {
        await using var scope = new MetadataScope();
        var root = new EntityRoot<TestState, TestEvent>(default, AggregateVersion.None);

        await root.AcceptAsync(new TwoEventsCommand("agg/1"), TestContext.Current.CancellationToken);

        // The state attribute is overwritten after each Apply (Single multiplicity),
        // so only the final state value is visible in the snapshot.
        scope.Snapshot()["stateKey"].Should().Be("second");
    }

    [Fact]
    public async Task GivenNoActiveScope_AcceptAsyncDoesNotThrow() {
        var root = new EntityRoot<TestState, TestEvent>(default, AggregateVersion.None);

        var act = () => root.AcceptAsync(new TestCommand("agg/1", "value"), TestContext.Current.CancellationToken).AsTask();

        await act.Should().NotThrowAsync();
    }

    [Fact]
    public async Task GivenNoActiveScope_ChangesAreStillApplied() {
        var root = new EntityRoot<TestState, TestEvent>(default, AggregateVersion.None);

        await root.AcceptAsync(new TestCommand("agg/1", "new-value"), TestContext.Current.CancellationToken);

        root.State.Value.Should().Be("new-value");
    }

    // A command that produces two events, useful for testing ordering.
    record TwoEventsCommand(AggregateIdentifier Id) : ICommand<TestState, TestEvent> {
        public async IAsyncEnumerable<TestEvent> ProgressAsync(
            TestState state,
            [EnumeratorCancellation] CancellationToken cancellationToken = default) {
            yield return new TestEvent("first");
            yield return new TestEvent("second");
        }
    }
}
