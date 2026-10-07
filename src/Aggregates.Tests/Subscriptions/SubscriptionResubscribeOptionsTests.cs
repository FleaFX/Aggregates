using FluentAssertions;

namespace Aggregates.Subscriptions;

public class SubscriptionResubscribeOptionsTests {
    [Fact]
    public void HasDocumentedDefaults() {
        var options = new SubscriptionResubscribeOptions();

        options.InitialDelay.Should().Be(TimeSpan.FromSeconds(1));
        options.MaxDelay.Should().Be(TimeSpan.FromSeconds(30));
        options.BackoffMultiplier.Should().Be(2.0);
    }
}
