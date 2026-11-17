using System.Collections.Immutable;
using System.Runtime.CompilerServices;
using FakeItEasy;
using FluentAssertions;

namespace Aggregates;

public class CommandHandlerTests {
    [Fact]
    public async Task HandleAsync_WhenAggregateNotFound_StartsAValueTypeStateFromInitial() {
        var repository = A.Fake<IRepository<ValuesState, int>>();
        EntityRoot<ValuesState, int>? createdRoot = null;
        A.CallTo(() => repository.TryGetEntityRootAsync(A<AggregateIdentifier>._, A<CancellationToken>._))
            .Returns(ValueTask.FromResult<EntityRoot<ValuesState, int>?>(null));
        A.CallTo(() => repository.Add(A<AggregateIdentifier>._, A<EntityRoot<ValuesState, int>>._))
            .Invokes(call => createdRoot = call.GetArgument<EntityRoot<ValuesState, int>>(1));
        var command = new AddValue("values-1", 7);

        await new CommandHandler<AddValue, ValuesState, int>(repository).HandleAsync(command, TestContext.Current.CancellationToken);

        command.Received.Should().NotBeNull();
        command.Received!.Value.Values.IsDefault.Should().BeFalse();
        command.Received.Value.Values.Should().BeEmpty();
        createdRoot!.State.Values.Should().Equal(7);
    }

    // A value type whose Initial differs from default: Apply on default throws.
    public readonly record struct ValuesState(ImmutableArray<int> Values) : IState<ValuesState, int> {
        public static ValuesState Initial => new([]);
        public ValuesState Apply(int @event) => new(Values.Add(@event));
    }

    public sealed record AddValue(AggregateIdentifier Id, int Value) : ICommand<ValuesState, int> {
        public ValuesState? Received { get; private set; }

        public async IAsyncEnumerable<int> ProgressAsync(ValuesState state, [EnumeratorCancellation] CancellationToken cancellationToken = default) {
            Received = state;
            await Task.CompletedTask;
            yield return Value;
        }
    }
}
