using System.Runtime.CompilerServices;
using Aggregates.Subscriptions;
using FakeItEasy;
using FluentAssertions;

namespace Aggregates.Sagas;

public class BaseSagaRepositoryTests {
    [Fact]
    public async Task ScheduledRequest_CountsForTheVersion_ButNotForTheState() {
        var repository = new ScriptedSagaRepository(
            ("link", 0, 1),
            ("request", 1, null),
            ("link", 2, 2));

        var root = await ((ISagaRepository<TestSagaState, TestEvent>)repository).TryGetAsync("saga-1", TestContext.Current.CancellationToken);

        root.Should().NotBeNull();
        root!.State.Total.Should().Be(3);
        root.Version.Should().Be(new AggregateVersion(2));
    }

    [Fact]
    public async Task ScheduledRequest_IsNotAHandledSource() {
        var repository = new ScriptedSagaRepository(
            ("link", 0, 1),
            ("request", 1, null));
        var root = await ((ISagaRepository<TestSagaState, TestEvent>)repository).TryGetAsync("saga-1", TestContext.Current.CancellationToken);
        var saga = A.Fake<ISaga<TestSagaState, TestEvent>>();
        A.CallTo(() => saga.ReactAsync(A<TestSagaState>._, A<TestEvent>._, A<CancellationToken>._))
            .Returns(AsyncEnumerable.Empty<ICommand>());

        // The request stored at saga-1@1 is not a handled source: a source at that position still reacts.
        await root!.AcceptAsync(new TestEvent(2), new EventReference("saga-1", 1), saga, TestContext.Current.CancellationToken);

        root.GetChanges().Should().ContainSingle();
    }

    [Fact]
    public async Task ScheduledRequestWithACause_MarksItsDeadlineAsHandled() {
        var repository = new ScriptedSagaRepository(
            ("link", 0, 1),
            ("caused", 1, 1));
        var root = await ((ISagaRepository<TestSagaState, TestEvent>)repository).TryGetAsync("saga-1", TestContext.Current.CancellationToken);
        var saga = A.Fake<ISaga<TestSagaState, TestEvent, TestDeadline>>();
        A.CallTo(() => saga.ReactToDeadlineAsync(A<TestSagaState>._, A<TestDeadline>._, A<CancellationToken>._))
            .Returns(AsyncEnumerable.Empty<ICommand>());

        // The request caused by saga-1@1 marks that deadline as handled: a redelivery doesn't react again.
        var commands = await root!.AcceptDeadlineAsync(new TestDeadline(1), new EventReference("saga-1", 1), saga, TestContext.Current.CancellationToken);

        commands.Should().BeEmpty();
        root.GetChanges().Should().BeEmpty();
        A.CallTo(() => saga.ReactToDeadlineAsync(A<TestSagaState>._, A<TestDeadline>._, A<CancellationToken>._)).MustNotHaveHappened();
    }

    [Fact]
    public async Task ScheduledRequestWithACause_CountsForTheVersion_ButNotForTheState() {
        var repository = new ScriptedSagaRepository(
            ("link", 0, 1),
            ("caused", 1, 0),
            ("link", 2, 2));

        var root = await ((ISagaRepository<TestSagaState, TestEvent>)repository).TryGetAsync("saga-1", TestContext.Current.CancellationToken);

        root.Should().NotBeNull();
        root!.State.Total.Should().Be(3);
        root.Version.Should().Be(new AggregateVersion(2));
    }

    record OtherSagaState() : IState<OtherSagaState, TestEvent> {
        public static OtherSagaState Initial => new();
        public OtherSagaState Apply(TestEvent @event) => this;
    }

    [Fact]
    public async Task TryGetAsync_GivenAnotherAggregateTypeUnderTheSameId_Throws() {
        var repository = new ScriptedSagaRepository(("link", 0, 1));
        var uow = new UnitOfWork();
        await using var _ = new UnitOfWorkScope(uow, (_, _) => ValueTask.CompletedTask);
        uow.Attach(new Aggregate("saga-1", new SagaRoot<OtherSagaState, TestEvent>(OtherSagaState.Initial, AggregateVersion.None)));

        var act = () => ((ISagaRepository<TestSagaState, TestEvent>)repository).TryGetAsync("saga-1", TestContext.Current.CancellationToken).AsTask();

        (await act.Should().ThrowAsync<InvalidOperationException>())
            .Which.Message.Should().Contain("two aggregate types can't share an id");
    }

    sealed class ScriptedSagaRepository(params (string Kind, ulong Revision, int? Value)[] entries) : BaseSagaRepository<TestSagaState, TestEvent> {
        protected override async IAsyncEnumerable<SagaStreamEntry> ReadStreamAsync(AggregateIdentifier sagaId, [EnumeratorCancellation] CancellationToken cancellationToken) {
            await Task.CompletedTask;
            foreach (var (kind, revision, value) in entries)
                yield return kind switch {
                    "request" => SagaStreamEntry.ScheduledRequest(new EventReference("saga-1", revision)),
                    "caused" => SagaStreamEntry.ScheduledRequest(new EventReference("saga-1", revision), new EventReference("saga-1", (ulong)value!.Value)),
                    _ => new SagaStreamEntry(new EventReference("source", revision), new TestEvent(value!.Value))
                };
        }
    }
}
