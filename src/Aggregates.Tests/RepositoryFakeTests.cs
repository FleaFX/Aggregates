using FluentAssertions;

namespace Aggregates;

public class RepositoryFakeTests {
    [Fact]
    public async Task GetAsync_ReturnsTheStateOfAnAddedRoot() {
        IRepository<TestState, TestEvent> repository = new InMemoryRepository();
        var identifier = new AggregateIdentifier("order-1");
        repository.Add(identifier, new EntityRoot<TestState, TestEvent>(TestState.Initial, AggregateVersion.None));

        var state = await repository.GetAsync(identifier, TestContext.Current.CancellationToken);

        state.Should().Be(TestState.Initial);
    }

    [Fact]
    public async Task GetAsync_OfAnUnknownAggregate_ThrowsAggregateRootNotFoundException() {
        IRepository<TestState, TestEvent> repository = new InMemoryRepository();

        var act = () => repository.GetAsync(new AggregateIdentifier("order-1"), TestContext.Current.CancellationToken).AsTask();

        await act.Should().ThrowAsync<AggregateRootNotFoundException>();
    }

    sealed record TestEvent(string Value);

    sealed record TestState(string Value) : IState<TestState, TestEvent> {
        public static TestState Initial => new("none");
        public TestState Apply(TestEvent @event) => this with { Value = @event.Value };
    }

    // A hand-written repository: the default GetAsync comes from the interface.
    sealed class InMemoryRepository : IRepository<TestState, TestEvent> {
        readonly Dictionary<AggregateIdentifier, EntityRoot<TestState, TestEvent>> _roots = [];

        public ValueTask<EntityRoot<TestState, TestEvent>?> TryGetEntityRootAsync(AggregateIdentifier identifier, CancellationToken cancellationToken = default) =>
            ValueTask.FromResult(_roots.TryGetValue(identifier, out var root) ? root : null);

        public void Add(AggregateIdentifier identifier, EntityRoot<TestState, TestEvent> entityRoot) =>
            _roots[identifier] = entityRoot;
    }
}
