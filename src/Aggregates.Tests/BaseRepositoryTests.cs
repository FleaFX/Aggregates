using System.Runtime.CompilerServices;
using FluentAssertions;

namespace Aggregates;

public class BaseRepositoryTests {
    record TestEvent(string Value);

    record struct TestState(string Value) : IState<TestState, TestEvent> {
        public static TestState Initial => new();
        public TestState Apply(TestEvent @event) => new(@event.Value);
    }

    sealed class FakeRepository(params TestEvent[] events) : BaseRepository<TestState, TestEvent> {
        protected override async IAsyncEnumerable<TestEvent> ReadEventsAsync(
            AggregateIdentifier identifier,
            [EnumeratorCancellation] CancellationToken cancellationToken = default) {
            if (events.Length == 0) throw new AggregateRootNotFoundException(identifier);
            foreach (var e in events) yield return e;
        }
    }

    public class GetAsync : BaseRepositoryTests {
        [Fact]
        public async Task GivenAggregateNotFound_Throws() {
            IRepository<TestState, TestEvent> repo = new FakeRepository();

            var act = () => repo.GetAsync("agg/1", TestContext.Current.CancellationToken).AsTask();

            await act.Should().ThrowAsync<AggregateRootNotFoundException>();
        }

        [Fact]
        public async Task GivenEventsExist_ReturnsStateOfLastEvent() {
            IRepository<TestState, TestEvent> repo = new FakeRepository(
                new TestEvent("first"), new TestEvent("last"));

            var state = await repo.GetAsync("agg/1", TestContext.Current.CancellationToken);

            state.Value.Should().Be("last");
        }
    }

    public class TryGetEntityRootAsync : BaseRepositoryTests {
        [Fact]
        public async Task GivenEventsExist_VersionIsEventCountMinusOne() {
            IRepository<TestState, TestEvent> repo = new FakeRepository(
                new TestEvent("a"), new TestEvent("b"), new TestEvent("c"));

            var root = await repo.TryGetEntityRootAsync("agg/1", TestContext.Current.CancellationToken);

            ((long)root!.Version).Should().Be(2);
        }

        [Fact]
        public async Task GivenAggregateAlreadyInUnitOfWork_ReturnsSameInstance() {
            IRepository<TestState, TestEvent> repo = new FakeRepository(new TestEvent("hello"));
            var uow = new UnitOfWork();
            await using var _ = new UnitOfWorkScope(uow, _ => ValueTask.CompletedTask);

            var first = await repo.TryGetEntityRootAsync("agg/1", TestContext.Current.CancellationToken);
            var second = await repo.TryGetEntityRootAsync("agg/1", TestContext.Current.CancellationToken);

            second.Should().BeSameAs(first);
        }
    }

    public class Add : BaseRepositoryTests {
        [Fact]
        public async Task GivenAmbientScope_AttachesRootToUnitOfWork() {
            IRepository<TestState, TestEvent> repo = new FakeRepository();
            var uow = new UnitOfWork();
            await using var _ = new UnitOfWorkScope(uow, _ => ValueTask.CompletedTask);
            var root = new EntityRoot<TestState, TestEvent>(AggregateVersion.None);

            repo.Add("agg/1", root);

            uow.Get("agg/1").Should().NotBeNull();
        }
    }
}
