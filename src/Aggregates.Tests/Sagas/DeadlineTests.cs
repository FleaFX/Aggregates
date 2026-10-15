using FluentAssertions;

namespace Aggregates.Sagas;

public class DeadlineTests {
    static readonly DateTimeOffset Due = new(2026, 11, 1, 0, 0, 0, TimeSpan.Zero);

    [Fact]
    public void At_SetsDueAndPayload() {
        var payload = new TestDeadline(1);

        var deadline = Deadline.At(Due, payload);

        deadline.Due.Should().Be(Due);
        deadline.Payload.Should().Be(payload);
    }

    [Fact]
    public void At_WithoutPayload_Throws() {
        var act = () => Deadline.At(Due, null!);

        act.Should().Throw<ArgumentNullException>().WithParameterName("payload");
    }

    [Fact]
    public void At_WithADeadlineAsPayload_Throws() {
        var act = () => Deadline.At(Due, Deadline.At(Due, new TestDeadline(1)));

        act.Should().Throw<ArgumentException>().WithMessage("A deadline's payload can't be another deadline or a deferred command.*");
    }

    [Fact]
    public void At_WithADeferredCommandAsPayload_Throws() {
        var act = () => Deadline.At(Due, new TestCommand().DeferUntil(Due));

        act.Should().Throw<ArgumentException>().WithMessage("A deadline's payload can't be another deadline or a deferred command.*");
    }

    [Fact]
    public void IsACommand() {
        Deadline.At(Due, new TestDeadline(1)).Should().BeAssignableTo<ICommand>();
    }
}
