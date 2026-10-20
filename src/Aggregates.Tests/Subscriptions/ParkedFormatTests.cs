using System.Text;
using System.Text.Json;
using Aggregates.Testing;
using FluentAssertions;

namespace Aggregates.Subscriptions;

/// <summary>
/// Tests how <see cref="ParkedFormat"/> writes and reads the data of a parked message.
/// </summary>
public class ParkedFormatTests {
    static readonly DateTimeOffset Created = new(2026, 10, 15, 8, 0, 0, TimeSpan.Zero);

    static ParkedEntry Entry(byte[] data) =>
        new(3, ParkedFormat.ParkedEventType, data, Created);

    // Thrown and caught, so the exception has a stack trace.
    static Exception Thrown() {
        try {
            throw new InvalidOperationException("boom");
        } catch (InvalidOperationException exception) {
            return exception;
        }
    }

    [Fact]
    public void WriteParked_ThenReadParked_RoundTrips() {
        var message = new SubscriptionMessage(new OrderPlaced("order-1", "alice"), 42, new EventReference("order-1", 7), EventMetadata.Empty);
        var exception = Thrown();

        var parked = ParkedFormat.ReadParked("sub-1", Entry(ParkedFormat.WriteParked(message, exception)));

        parked.SubscriptionId.Should().Be("sub-1");
        parked.Id.Should().Be("3");
        parked.ParkedAt.Should().Be(Created);
        parked.Source.Should().Be(new EventReference("order-1", 7));
        parked.CommitPosition.Should().Be(42);
        parked.EventType.Should().Be(typeof(OrderPlaced).FullName);
        parked.ExceptionType.Should().Be(typeof(InvalidOperationException).FullName);
        parked.ExceptionMessage.Should().Be("boom");
        parked.StackTrace.Should().NotBeNullOrEmpty();
    }

    [Fact]
    public void WriteParked_WithoutEvent_HasNoEventType() {
        var message = new SubscriptionMessage(null, 42, new EventReference("order-1", 7), EventMetadata.Empty);

        var data = ParkedFormat.WriteParked(message, Thrown());

        using var payload = JsonDocument.Parse(data);
        payload.RootElement.EnumerateObject().Select(property => property.Name).Should().NotContain("EventType");
        ParkedFormat.ReadParked("sub-1", Entry(data)).EventType.Should().BeNull();
    }

    [Fact]
    public void WriteParked_KeepsTheFieldsOfEarlierEntries() {
        var message = new SubscriptionMessage(new OrderPlaced("order-1", "alice"), 42, new EventReference("order-1", 7), EventMetadata.Empty);

        var data = ParkedFormat.WriteParked(message, Thrown());

        using var payload = JsonDocument.Parse(data);
        payload.RootElement.GetProperty("CommitPosition").GetUInt64().Should().Be(42);
        payload.RootElement.GetProperty("ExceptionType").GetString().Should().Be(typeof(InvalidOperationException).FullName);
        payload.RootElement.GetProperty("Message").GetString().Should().Be("boom");
    }

    [Fact]
    public void ReadParked_WithoutStream_HasNoSource() {
        var parked = ParkedFormat.ReadParked("sub-1",
            Entry("{\"CommitPosition\":5,\"ExceptionType\":\"X\",\"Message\":\"m\"}"u8.ToArray()));

        parked.Source.Should().BeNull();
        parked.CommitPosition.Should().Be(5);
        parked.StackTrace.Should().BeNull();
    }

    [Theory]
    [InlineData("not json")]
    [InlineData("[]")]
    [InlineData("{\"Stream\":\"s\"}")]
    public void ReadParked_OfInvalidData_GivesDefaults(string data) {
        var parked = ParkedFormat.ReadParked("sub-1", Entry(Encoding.UTF8.GetBytes(data)));

        parked.Source.Should().BeNull();
        parked.CommitPosition.Should().Be(0);
        parked.ExceptionType.Should().BeEmpty();
        parked.ExceptionMessage.Should().BeEmpty();
    }

    [Fact]
    public void EventTypes_StartWithTheParkedPrefix() {
        ParkedFormat.ParkedEventType.Should().StartWith("$aggregates-parked");
        ParkedFormat.ReplayedEventType.Should().StartWith("$aggregates-parked");
        ParkedFormat.DismissedEventType.Should().StartWith("$aggregates-parked");
        ParkedFormat.ParkedStream("x").Should().Be("parked-x");
    }
}
