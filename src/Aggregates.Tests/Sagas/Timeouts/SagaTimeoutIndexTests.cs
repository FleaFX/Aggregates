using Aggregates.Subscriptions;
using FluentAssertions;

namespace Aggregates.Sagas;

public class SagaTimeoutIndexTests {
    const string Owner = "Test.Reminders@v1";
    static readonly DateTimeOffset Start = new(2026, 10, 14, 8, 0, 0, TimeSpan.Zero);

    static SagaTimeoutIndex Index(ulong? start = 10) =>
        new(new HashSet<string> { Owner }, start, Deserialize);

    static object? Deserialize(string type, ReadOnlyMemory<byte> data) => type switch {
        "Test.Command@v1" => new TestCommand(),
        "Test.String@v1" => "text",
        "Test.Failing@v1" => throw new InvalidOperationException("deserializing failed"),
        _ => null
    };

    static SagaTimeoutEntry Request(ulong position, string owner, DateTimeOffset due, string commandType = "Test.Command@v1", ulong revision = 0) =>
        new(SagaTimeoutFormat.RequestEventType, SagaTimeoutFormat.WriteRequest(owner, due, commandType, "{}"u8),
            EventMetadata.Empty, new EventReference("saga-1", revision), position);

    [Fact]
    public void Request_OfAnOwner_IsOpen_WithItsCommand() {
        var index = Index();
        var due = Start.AddDays(1);

        var change = index.Apply(Request(12, Owner, due), out var requested);

        change.Should().Be(SagaTimeoutChange.Requested);
        index.Count.Should().Be(1);
        index.NextDue.Should().Be(due);
        requested.Command.Should().BeOfType<TestCommand>();
        requested.Error.Should().BeNull();
    }

    [Fact]
    public void Request_OfAnotherSaga_IsIgnored() {
        var index = Index();

        var change = index.Apply(Request(25, "Test.Other@v1", Start), out _);

        change.Should().Be(SagaTimeoutChange.None);
        index.Count.Should().Be(0);
        index.Checkpoint.Should().Be(25);
    }

    [Fact]
    public void Request_OfAPredecessor_IsOpen() {
        var index = new SagaTimeoutIndex(new HashSet<string> { "Test.Reminders@v2", "Test.Reminders@v1" }, 10, Deserialize);

        var change = index.Apply(Request(12, "Test.Reminders@v1", Start), out _);

        change.Should().Be(SagaTimeoutChange.Requested);
        index.Count.Should().Be(1);
    }

    [Fact]
    public void Delivery_ClosesTheRequest() {
        var index = Index();
        index.Apply(Request(12, Owner, Start), out _);

        var change = index.Apply(new SagaTimeoutEntry(SagaTimeoutFormat.DeliveryEventType, "0@saga-1"u8.ToArray(),
            EventMetadata.Empty, new EventReference("timeouts-Test.Reminders@v1", 0), 13), out _);

        change.Should().Be(SagaTimeoutChange.Delivered);
        index.Count.Should().Be(0);
    }

    [Fact]
    public void Delivery_OfAnUnknownRequest_ChangesNothing() {
        var index = Index();

        var change = index.Apply(new SagaTimeoutEntry(SagaTimeoutFormat.DeliveryEventType, "5@elsewhere"u8.ToArray(),
            EventMetadata.Empty, new EventReference("timeouts-Test.Reminders@v1", 0), 13), out _);

        change.Should().Be(SagaTimeoutChange.None);
    }

    [Fact]
    public void InvalidRequest_IsReported() {
        var index = Index();

        var change = index.Apply(new SagaTimeoutEntry(SagaTimeoutFormat.RequestEventType, "{}"u8.ToArray(),
            EventMetadata.Empty, new EventReference("saga-1", 0), 12), out _);

        change.Should().Be(SagaTimeoutChange.Invalid);
    }

    [Fact]
    public void TryPeekDue_RespectsTheClock_AndTheOrderOfDueTimes() {
        var index = Index();
        index.Apply(Request(1, Owner, Start.AddDays(2), revision: 0), out _);
        index.Apply(Request(2, Owner, Start.AddDays(1), revision: 1), out _);
        index.Apply(Request(3, Owner, Start.AddDays(1), revision: 2), out _);

        index.TryPeekDue(Start, out _).Should().BeFalse();
        index.TryPeekDue(Start.AddDays(1), out var due).Should().BeTrue();
        due.CommitPosition.Should().Be(2);

        index.Remove(due.Request);

        index.TryPeekDue(Start.AddDays(1), out var next).Should().BeTrue();
        next.CommitPosition.Should().Be(3);
    }

    [Fact]
    public void Checkpoint_IsTheLastPosition_WhenNothingIsOpen() {
        var index = Index();
        index.Apply(SagaTimeoutEntry.Progress(20), out _);
        index.Apply(Request(25, "Test.Other@v1", Start), out _);

        index.Checkpoint.Should().Be(25);
    }

    [Fact]
    public void Checkpoint_StaysBeforeTheOldestOpenRequest() {
        var index = Index();
        index.Apply(Request(12, Owner, Start), out _);
        index.Apply(Request(15, Owner, Start, revision: 1), out _);

        index.Checkpoint.Should().Be(10);

        index.Remove(new EventReference("saga-1", 0));
        index.Checkpoint.Should().Be(12);

        index.Remove(new EventReference("saga-1", 1));
        index.Checkpoint.Should().Be(15);
    }

    [Fact]
    public void Checkpoint_IsNull_WhileARequestReadFromTheStartIsOpen() {
        var index = Index(start: null);

        index.Apply(Request(3, Owner, Start), out _);

        index.Checkpoint.Should().BeNull();
    }

    [Fact]
    public void Progress_AtOrBeforeTheLastPosition_IsIgnored() {
        var index = Index();

        index.Apply(SagaTimeoutEntry.Progress(0), out _);

        index.Checkpoint.Should().Be(10);
    }

    [Fact]
    public void Marker_IsReported() {
        var index = Index();

        var change = index.Apply(new SagaTimeoutEntry(SagaTimeoutFormat.MarkerEventType, "marker"u8.ToArray(),
            EventMetadata.Empty, new EventReference("timeouts-Test.Reminders@v1", 0), 13), out _);

        change.Should().Be(SagaTimeoutChange.Marker);
    }

    [Theory]
    [InlineData("Test.Failing@v1")]
    [InlineData("Test.Unknown@v1")]
    [InlineData("Test.String@v1")]
    public void UndeserializableCommand_IsOpen_WithAnError(string commandType) {
        var index = Index();

        var change = index.Apply(Request(12, Owner, Start, commandType), out var requested);

        change.Should().Be(SagaTimeoutChange.Requested);
        requested.Command.Should().BeNull();
        requested.Error.Should().NotBeNull();
        if (commandType == "Test.Failing@v1")
            requested.Error.Should().BeOfType<InvalidOperationException>().Which.Message.Should().Be("deserializing failed");
        if (commandType == "Test.Unknown@v1")
            requested.Error!.Message.Should().Contain("does not know the command type");
        if (commandType == "Test.String@v1")
            requested.Error!.Message.Should().Contain("which is not a command");
    }
}
