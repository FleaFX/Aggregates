using FluentAssertions;

namespace Aggregates.Tests; 

public class UnitOfWorkTests {
    public readonly record struct TestState(string Value) : IState<TestState, string> {
        public static TestState Initial => new();

        public TestState Apply(string @event) => new(Value: @event);
    }

    public class Get : UnitOfWorkTests {
        [Fact]
        public void GivenAggregateDetached_ThenReturnsNull() {
            var uow = new UnitOfWork();

            var aggregate = uow.Get("aggregate/1");

            aggregate.Should().BeNull();
        }

        [Fact]
        public void GivenAggregateAttached_ThenReturnsAggregate() {
            var uow = new UnitOfWork();
            var aggregateRoot = new AggregateRoot<TestState, string>();
            uow.Attach(new Aggregate("aggregate/1", aggregateRoot));

            var aggregate = uow.Get("aggregate/1");

            aggregate.Should().Be(new Aggregate("aggregate/1", aggregateRoot));
        }
    }

    public class Attach : UnitOfWorkTests {
        [Fact]
        public void GivenAggregateAlreadyAttached_ThenThrows() {
            var uow = new UnitOfWork();
            var aggregateRoot = new AggregateRoot<TestState, string>();
            uow.Attach(new Aggregate("aggregate/1", aggregateRoot));

            var act = () => uow.Attach(new Aggregate("aggregate/1", aggregateRoot));
                
            act.Should().Throw<InvalidOperationException>();
        }
    }
}