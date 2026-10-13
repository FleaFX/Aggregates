using System.Text;
using FluentAssertions;

namespace Aggregates.Subscriptions;

public class EventReferenceTests {
    [Fact]
    public void ToLinkData_ReturnsRevisionAndStreamIdAsUtf8() {
        var reference = new EventReference("order-1", 5);

        reference.ToLinkData().Should().Equal("5@order-1"u8.ToArray());
    }

    [Fact]
    public void LinkData_RoundTripsThroughTryParseLinkData() {
        var reference = new EventReference("order-1", 5);

        EventReference.TryParseLinkData(reference.ToLinkData(), out var parsed).Should().BeTrue();
        parsed.Should().Be(reference);
    }

    [Fact]
    public void TryParseLinkData_WithAtSignInStreamId_ParsesRevisionUpToFirstAtSign() {
        // The stream id may itself contain '@', e.g. "Orders@v1-1".
        EventReference.TryParseLinkData("5@Orders@v1-1"u8, out var parsed).Should().BeTrue();
        parsed.Should().Be(new EventReference("Orders@v1-1", 5));
    }

    [Theory]
    [InlineData("")]
    [InlineData("order-1")]
    [InlineData("x@s")]
    [InlineData("5@")]
    [InlineData("-1@s")]
    [InlineData("@s")]
    public void TryParseLinkData_WithInvalidData_ReturnsFalse(string data) {
        EventReference.TryParseLinkData(Encoding.UTF8.GetBytes(data), out var parsed).Should().BeFalse();
        parsed.Should().Be(default(EventReference));
    }
}
