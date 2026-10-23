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

    [Fact]
    public async Task DisposingTwice_DoesNotTouchTheParent() {
        var commitCount = 0;
        await using (var outer = new UnitOfWorkScope(new UnitOfWork(), _ => { commitCount++; return ValueTask.CompletedTask; })) {
            outer.Complete();
            var inner = new UnitOfWorkScope(new UnitOfWork(), _ => ValueTask.CompletedTask);
            await inner.DisposeAsync();
            await inner.DisposeAsync();
            UnitOfWorkScope.Current.Should().BeSameAs(outer);
        }

        commitCount.Should().Be(1, "the parent scope commits once, on its own disposal");
    }

    [Fact]
    public async Task DisposingOutOfOrder_Throws() {
        var outer = new UnitOfWorkScope(new UnitOfWork(), _ => ValueTask.CompletedTask);
        var inner = new UnitOfWorkScope(new UnitOfWork(), _ => ValueTask.CompletedTask);

        var act = () => outer.DisposeAsync().AsTask();

        await act.Should().ThrowAsync<InvalidOperationException>();
        await inner.DisposeAsync();
        await outer.DisposeAsync();
    }

    record struct TestState(string Value) : IState<TestState, string> {
        public static TestState Initial => new();
        public TestState Apply(string @event) => new(Value: @event);
    }
}
