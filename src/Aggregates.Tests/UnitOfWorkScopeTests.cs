using FluentAssertions;

namespace Aggregates;

public class UnitOfWorkScopeTests {
    [Fact]
    public async Task GivenCompleted_InvokesCommitDelegate() {
        var uow = new UnitOfWork();
        var committed = false;

        await using (var scope = new UnitOfWorkScope(uow, _ => { committed = true; return ValueTask.CompletedTask; })) {
            scope.Complete();
        }

        committed.Should().BeTrue();
    }

    [Fact]
    public async Task GivenNotCompleted_DoesNotInvokeCommitDelegate() {
        var uow = new UnitOfWork();
        var committed = false;

        await using (new UnitOfWorkScope(uow, _ => { committed = true; return ValueTask.CompletedTask; })) {
            // no Complete()
        }

        committed.Should().BeFalse();
    }

    [Fact]
    public async Task OnDispose_ClearsUnitOfWork() {
        var uow = new UnitOfWork();
        uow.Attach(new Aggregate("aggregate/1", new EntityRoot<TestState, string>(default, AggregateVersion.None)));

        await using (new UnitOfWorkScope(uow, _ => ValueTask.CompletedTask)) {
            // no Complete()
        }

        uow.Get("aggregate/1").Should().BeNull();
    }

    [Fact]
    public async Task GivenNestedScopes_EachScopeIsIndependent() {
        var uow = new UnitOfWork();
        var outerCommitted = false;
        var innerCommitted = false;

        await using (var outer = new UnitOfWorkScope(uow, _ => { outerCommitted = true; return ValueTask.CompletedTask; })) {
            await using (var inner = new UnitOfWorkScope(uow, _ => { innerCommitted = true; return ValueTask.CompletedTask; })) {
                inner.Complete();
            }
            outer.Complete();
        }

        innerCommitted.Should().BeTrue();
        outerCommitted.Should().BeTrue();
    }

    [Fact]
    public async Task GivenNestedScopes_OuterNotCompleted_InnerStillCommits() {
        var uow = new UnitOfWork();
        var outerCommitted = false;
        var innerCommitted = false;

        await using (new UnitOfWorkScope(uow, _ => { outerCommitted = true; return ValueTask.CompletedTask; })) {
            await using (var inner = new UnitOfWorkScope(uow, _ => { innerCommitted = true; return ValueTask.CompletedTask; })) {
                inner.Complete();
            }
            // outer not completed
        }

        innerCommitted.Should().BeTrue();
        outerCommitted.Should().BeFalse();
    }

    record struct TestState(string Value) : IState<TestState, string> {
        public static TestState Initial => new();
        public TestState Apply(string @event) => new(Value: @event);
    }
}
