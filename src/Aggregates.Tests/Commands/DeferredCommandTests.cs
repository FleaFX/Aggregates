using FluentAssertions;

namespace Aggregates;

public class DeferredCommandTests {
    [Fact]
    public void DeferUntil_SetsDueAndCommand() {
        var command = new NoopCommand();
        var due = new DateTimeOffset(2026, 11, 1, 0, 0, 0, TimeSpan.Zero);

        var deferred = command.DeferUntil(due);

        deferred.Due.Should().Be(due);
        deferred.Command.Should().Be(command);
    }

    [Fact]
    public void NullCommand_Throws() {
        var act = () => ((ICommand)null!).DeferUntil(DateTimeOffset.UtcNow);

        act.Should().Throw<ArgumentNullException>();
    }

    [Fact]
    public void NestedDeferredCommand_Throws() {
        var act = () => new NoopCommand().DeferUntil(DateTimeOffset.UtcNow).DeferUntil(DateTimeOffset.UtcNow);

        act.Should().Throw<ArgumentException>().WithParameterName("command");
    }

    [Fact]
    public void IsACommand() {
        new NoopCommand().DeferUntil(DateTimeOffset.UtcNow).Should().BeAssignableTo<ICommand>();
    }
}
