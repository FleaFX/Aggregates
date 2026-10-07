using FluentAssertions;

namespace Aggregates.Subscriptions;

public class BackoffTests {
    static readonly TimeSpan Initial = TimeSpan.FromSeconds(1);
    static readonly TimeSpan Max = TimeSpan.FromSeconds(30);

    [Theory]
    [InlineData(1, 1_000)]
    [InlineData(2, 2_000)]
    [InlineData(3, 4_000)]
    [InlineData(5, 16_000)]
    public void GrowsExponentially_WithinJitter(int attempt, double expectedMs) {
        var delay = Backoff.Delay(attempt, Initial, Max, 2.0);

        delay.TotalMilliseconds.Should().BeInRange(expectedMs * 0.9, expectedMs * 1.1);
    }

    [Theory]
    [InlineData(6)]
    [InlineData(100)]
    public void IsCappedAtMaxDelay_WithinJitter(int attempt) {
        var delay = Backoff.Delay(attempt, Initial, Max, 2.0);

        delay.TotalMilliseconds.Should().BeInRange(27_000, 33_000);
    }

    [Fact]
    public void GivenZeroDelays_ReturnsZero() {
        Backoff.Delay(3, TimeSpan.Zero, TimeSpan.Zero, 2.0).Should().Be(TimeSpan.Zero);
    }
}
