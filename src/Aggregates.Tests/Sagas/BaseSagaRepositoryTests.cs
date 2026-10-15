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

    sealed class ScriptedSagaRepository(params (string Kind, ulong Revision, int? Value)[] entries) : BaseSagaRepository<TestSagaState, TestEvent> {
        protected override async IAsyncEnumerable<SagaStreamEntry> ReadStreamAsync(AggregateIdentifier sagaId, [EnumeratorCancellation] CancellationToken cancellationToken) {
            await Task.CompletedTask;
            foreach (var (kind, revision, value) in entries)
                yield return kind == "request"
                    ? SagaStreamEntry.ScheduledRequest(new EventReference("saga-1", revision))
                    : new SagaStreamEntry(new EventReference("source", revision), new TestEvent(value!.Value));
        }
    }
}
