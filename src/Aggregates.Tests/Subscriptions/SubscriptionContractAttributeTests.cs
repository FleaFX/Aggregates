using FluentAssertions;

namespace Aggregates.Subscriptions;

public class SubscriptionContractAttributeTests {
    [Theory]
    [InlineData("")]
    [InlineData("  ")]
    public void GivenAnEmptyOrWhitespaceName_Throws(string name) {
        var act = () => new TestContractAttribute(name);

        act.Should().Throw<ArgumentException>();
    }

    [Fact]
    public void GivenANullName_Throws() {
        var act = () => new TestContractAttribute(null!);

        act.Should().Throw<ArgumentException>();
    }

    [Fact]
    public void GivenVersionZero_Throws() {
        var act = () => new TestContractAttribute("Test", version: 0);

        act.Should().Throw<ArgumentOutOfRangeException>();
    }

    private sealed class TestContractAttribute(
        string name,
        int version = 1,
        string? @namespace = null,
        string? continueFrom = null,
        bool startFromEnd = false,
        SubscriptionErrorMode onError = SubscriptionErrorMode.Park
    ) : SubscriptionContractAttribute(name, version, @namespace, continueFrom, startFromEnd, onError);
}
