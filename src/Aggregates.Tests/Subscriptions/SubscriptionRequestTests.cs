using System.Text.RegularExpressions;
using FluentAssertions;

namespace Aggregates.Subscriptions;

public class SubscriptionRequestTests {
    [Fact]
    public void WithoutEventTypes_PatternIsNull() {
        var request = new SubscriptionRequest(null, false);

        request.EventTypePattern.Should().BeNull();
    }

    [Fact]
    public void WithEventTypes_EscapesEveryName() {
        var request = new SubscriptionRequest(null, false, ["IntegrationTests.OrderPlaced@v1"]);

        request.EventTypePattern.Should().Be("^(?:IntegrationTests\\.OrderPlaced@v1)$");
    }

    [Fact]
    public void WithEventTypes_MatchesExactly() {
        var versioned = new SubscriptionRequest(null, false, ["IntegrationTests.OrderPlaced@v1"]);
        var system = new SubscriptionRequest(null, false, ["x"]);

        versioned.EventTypePattern.Should().NotBeNull();
        Regex.IsMatch("IntegrationTests.OrderPlaced@v1", versioned.EventTypePattern!).Should().BeTrue();
        Regex.IsMatch("IntegrationTests.OrderPlaced@v10", versioned.EventTypePattern!).Should().BeFalse();
        Regex.IsMatch("x", system.EventTypePattern!).Should().BeTrue();
        Regex.IsMatch("$x", system.EventTypePattern!).Should().BeFalse();
    }

    [Fact]
    public void WithMultipleEventTypes_MatchesEachOfThem() {
        var request = new SubscriptionRequest(null, false, ["b", "a"]);

        request.EventTypePattern.Should().Be("^(?:b|a)$");
        Regex.IsMatch("a", request.EventTypePattern!).Should().BeTrue();
        Regex.IsMatch("b", request.EventTypePattern!).Should().BeTrue();
    }
}
