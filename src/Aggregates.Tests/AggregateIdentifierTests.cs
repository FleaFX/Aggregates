using System.Text.Json;
using FluentAssertions;

namespace Aggregates;

public class AggregateIdentifierTests {
    [Fact]
    public void AcceptsAnOrdinaryId() {
        new AggregateIdentifier("order-1").Value.Should().Be("order-1");

        AggregateIdentifier implicitConversion = "order-1";
        implicitConversion.Value.Should().Be("order-1");
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData(" ")]
    public void RejectsNullEmptyOrWhitespace(string? value) {
        var act = () => new AggregateIdentifier(value!);

        act.Should().Throw<ArgumentException>();
    }

    [Theory]
    [InlineData("$x")]
    [InlineData("$$orders-1")]
    public void RejectsAnIdStartingWithDollar(string value) {
        var act = () => new AggregateIdentifier(value);

        act.Should().Throw<ArgumentException>().WithMessage("*'$'*");
    }

    [Fact]
    public void AcceptsADollarAfterTheFirstCharacter() {
        new AggregateIdentifier("order-$1").Value.Should().Be("order-$1");
    }

    [Fact]
    public void KeepsItsValue_ThroughSystemTextJson() {
        var json = JsonSerializer.Serialize(new AggregateIdentifier("order-1"));

        JsonSerializer.Deserialize<AggregateIdentifier>(json).Value.Should().Be("order-1");
    }

    [Fact]
    public void RejectsAnIdStartingWithDollar_WhenDeserializedOrCopied() {
        var deserialize = () => JsonSerializer.Deserialize<AggregateIdentifier>("""{"Value":"$x"}""");
        var copy = () => new AggregateIdentifier("order-1") with { Value = "$x" };

        deserialize.Should().Throw<ArgumentException>().WithMessage("*'$'*");
        copy.Should().Throw<ArgumentException>().WithMessage("*'$'*");
    }
}
