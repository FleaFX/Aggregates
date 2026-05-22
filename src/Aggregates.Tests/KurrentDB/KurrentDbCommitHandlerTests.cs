using FluentAssertions;

namespace Aggregates.KurrentDB;

public class KurrentDbCommitHandlerTests {
    // Helper: a serialized event with sensible defaults.
    static SerializedEvent Event(string type = "OrderPlaced", byte[]? data = null) =>
        new(type, data ?? [1, 2, 3]);

    [Fact]
    public void CreateEventId_GivenSameInputs_ReturnsSameUuid() {
        var id1 = KurrentDbCommitHandler.CreateEventId("orders-1", 0, Event());
        var id2 = KurrentDbCommitHandler.CreateEventId("orders-1", 0, Event());

        id1.Should().Be(id2);
    }

    [Fact]
    public void CreateEventId_GivenDifferentStreamName_ReturnsDifferentUuid() {
        var id1 = KurrentDbCommitHandler.CreateEventId("orders-1", 0, Event());
        var id2 = KurrentDbCommitHandler.CreateEventId("orders-2", 0, Event());

        id1.Should().NotBe(id2);
    }

    [Fact]
    public void CreateEventId_GivenDifferentPosition_ReturnsDifferentUuid() {
        var id1 = KurrentDbCommitHandler.CreateEventId("orders-1", 0, Event());
        var id2 = KurrentDbCommitHandler.CreateEventId("orders-1", 1, Event());

        id1.Should().NotBe(id2);
    }

    [Fact]
    public void CreateEventId_GivenDifferentPayload_ReturnsDifferentUuid() {
        var id1 = KurrentDbCommitHandler.CreateEventId("orders-1", 0, Event(data: [1, 2, 3]));
        var id2 = KurrentDbCommitHandler.CreateEventId("orders-1", 0, Event(data: [4, 5, 6]));

        id1.Should().NotBe(id2);
    }

    [Fact]
    public void CreateEventId_GivenDifferentEventType_ReturnsDifferentUuid() {
        var id1 = KurrentDbCommitHandler.CreateEventId("orders-1", 0, Event(type: "OrderPlaced"));
        var id2 = KurrentDbCommitHandler.CreateEventId("orders-1", 0, Event(type: "OrderCancelled"));

        id1.Should().NotBe(id2);
    }
}
