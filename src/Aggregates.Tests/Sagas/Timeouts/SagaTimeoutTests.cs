using FluentAssertions;

namespace Aggregates.Sagas;

public class SagaTimeoutTests {
    [Fact]
    public void At_SetsDueAndCommand() {
        var command = new TestCommand();
        var due = new DateTimeOffset(2026, 11, 1, 0, 0, 0, TimeSpan.Zero);

        var timeout = SagaTimeout.At(due, command);

        timeout.Due.Should().Be(due);
        timeout.Command.Should().Be(command);
    }

    [Fact]
    public void NullCommand_Throws() {
        var act = () => SagaTimeout.At(DateTimeOffset.UtcNow, null!);

        act.Should().Throw<ArgumentNullException>();
    }

    [Fact]
    public void NestedTimeout_Throws() {
        var act = () => SagaTimeout.At(DateTimeOffset.UtcNow, SagaTimeout.At(DateTimeOffset.UtcNow, new TestCommand()));

        act.Should().Throw<ArgumentException>().WithParameterName("command");
    }

    [Fact]
    public void IsACommand() {
        SagaTimeout.At(DateTimeOffset.UtcNow, new TestCommand()).Should().BeAssignableTo<ICommand>();
    }
}
