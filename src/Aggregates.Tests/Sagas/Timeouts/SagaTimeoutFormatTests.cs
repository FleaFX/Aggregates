using System.Text;
using System.Text.Json;
using FluentAssertions;

namespace Aggregates.Sagas;

public class SagaTimeoutFormatTests {
    [Fact]
    public void Request_RoundTrips() {
        var due = new DateTimeOffset(2026, 11, 1, 9, 30, 15, TimeSpan.FromHours(2));

        var data = SagaTimeoutFormat.WriteRequest("Ns.Saga@v1", due, "Ns.Command@v1", [1, 2, 3]);

        SagaTimeoutFormat.TryReadRequest(data, out var envelope).Should().BeTrue();
        envelope.Owner.Should().Be("Ns.Saga@v1");
        envelope.Due.Should().Be(due);
        envelope.CommandType.Should().Be("Ns.Command@v1");
        envelope.CommandData.ToArray().Should().Equal(1, 2, 3);
    }

    [Fact]
    public void Request_IsReadableJson() {
        var data = SagaTimeoutFormat.WriteRequest("Ns.Saga@v1", new DateTimeOffset(2026, 11, 1, 0, 0, 0, TimeSpan.Zero), "Ns.Command@v1", [1, 2, 3]);

        using var document = JsonDocument.Parse(data);
        var root = document.RootElement;
        root.GetProperty("owner").GetString().Should().Be("Ns.Saga@v1");
        root.GetProperty("due").GetString().Should().Be("2026-11-01T00:00:00+00:00");
        root.GetProperty("type").GetString().Should().Be("Ns.Command@v1");
        root.GetProperty("data").TryGetBytesFromBase64(out var command).Should().BeTrue();
        command.Should().Equal(1, 2, 3);
    }

    [Theory]
    [InlineData("")]
    [InlineData("{}")]
    [InlineData("not json")]
    [InlineData("{\"owner\":\"\",\"due\":\"2026-10-14T00:00:00Z\",\"type\":\"Ns.Command@v1\",\"data\":\"\"}")]
    [InlineData("{\"owner\":\"Ns.Saga@v1\",\"due\":\"not a date\",\"type\":\"Ns.Command@v1\",\"data\":\"\"}")]
    [InlineData("{\"owner\":\"Ns.Saga@v1\",\"due\":\"2026-10-14T00:00:00Z\",\"type\":\"Ns.Command@v1\",\"data\":\"!\"}")]
    [InlineData("[1,2]")]
    public void TryReadRequest_RejectsInvalidData(string invalid) {
        SagaTimeoutFormat.TryReadRequest(Encoding.UTF8.GetBytes(invalid), out var envelope).Should().BeFalse();
        envelope.Should().Be(default(SagaTimeoutEnvelope));
    }

    [Fact]
    public void EventTypes_ShareThePrefix() {
        SagaTimeoutFormat.RequestEventType.Should().StartWith(SagaTimeoutFormat.EventTypePrefix);
        SagaTimeoutFormat.DeliveryEventType.Should().StartWith(SagaTimeoutFormat.EventTypePrefix);
        SagaTimeoutFormat.MarkerEventType.Should().StartWith(SagaTimeoutFormat.EventTypePrefix);
        SagaTimeoutFormat.EventTypePrefix.Should().StartWith("$");
    }
}
