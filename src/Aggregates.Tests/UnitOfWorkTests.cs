using FluentAssertions;

namespace Aggregates;

public class UnitOfWorkTests {
    record struct TestState(string Value) : IState<TestState, string> {
        public static TestState Initial => new();
        public TestState Apply(string @event) => new(Value: @event);
    }

    static EntityRoot<TestState, string> NewRoot() => new(AggregateVersion.None);

    public class Get : UnitOfWorkTests {
        [Fact]
        public void GivenAggregateNotAttached_ReturnsNull() {
            var uow = new UnitOfWork();

            uow.Get("aggregate/1").Should().BeNull();
        }

        [Fact]
        public void GivenAggregateAttached_ReturnsAggregate() {
            var uow = new UnitOfWork();
            var root = NewRoot();
            uow.Attach(new Aggregate("aggregate/1", root));

            uow.Get("aggregate/1").Should().Be(new Aggregate("aggregate/1", root));
        }
    }

    public class Attach : UnitOfWorkTests {
        [Fact]
        public void GivenAggregateAlreadyAttached_Throws() {
            var uow = new UnitOfWork();
            var root = NewRoot();
            uow.Attach(new Aggregate("aggregate/1", root));

            var act = () => uow.Attach(new Aggregate("aggregate/1", root));

            act.Should().Throw<InvalidOperationException>();
        }
    }

    public class Clear : UnitOfWorkTests {
        [Fact]
        public void GivenAttachedAggregates_RemovesAll() {
            var uow = new UnitOfWork();
            uow.Attach(new Aggregate("aggregate/1", NewRoot()));
            uow.Attach(new Aggregate("aggregate/2", NewRoot()));

            uow.Clear();

            uow.Get("aggregate/1").Should().BeNull();
            uow.Get("aggregate/2").Should().BeNull();
        }
    }

    public class GetChanged : UnitOfWorkTests {
        [Fact]
        public async Task GivenNoChanges_ReturnsNull() {
            var uow = new UnitOfWork();
            uow.Attach(new Aggregate("aggregate/1", NewRoot()));

            uow.GetChanged().Should().BeNull();
        }

        [Fact]
        public async Task GivenOneAggregateWithChanges_ReturnsThatAggregate() {
            var uow = new UnitOfWork();
            var root = NewRoot();
            uow.Attach(new Aggregate("aggregate/1", root));
            await root.AcceptAsync(new AppendCommand("hello"), TestContext.Current.CancellationToken);

            var changed = uow.GetChanged();

            changed.Should().NotBeNull();
            changed!.Value.Identifier.Value.Should().Be("aggregate/1");
        }

        [Fact]
        public async Task GivenMultipleAggregatesWithChanges_Throws() {
            var uow = new UnitOfWork();
            var root1 = NewRoot();
            var root2 = NewRoot();
            uow.Attach(new Aggregate("aggregate/1", root1));
            uow.Attach(new Aggregate("aggregate/2", root2));
            await root1.AcceptAsync(new AppendCommand("hello"), TestContext.Current.CancellationToken);
            await root2.AcceptAsync(new AppendCommand("world"), TestContext.Current.CancellationToken);

            var act = () => uow.GetChanged();

            act.Should().Throw<InvalidOperationException>();
        }

        record struct AppendCommand(string Value) : ICommand<TestState, string> {
            public AggregateIdentifier Id => "test";
            public async IAsyncEnumerable<string> ProgressAsync(TestState state, [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken cancellationToken = default) {
                yield return Value;
            }
        }
    }
}
