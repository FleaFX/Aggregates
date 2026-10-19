using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;

namespace Aggregates.Projections;

public class ProjectedEventTests {
    [Fact]
    public void NextTransaction_CountsFromOne() {
        var projected = new ProjectedEvent("sub-1", 1, new ServiceCollection().BuildServiceProvider());

        projected.NextTransaction().Should().Be(1);
        projected.NextTransaction().Should().Be(2);
        projected.NextTransaction().Should().Be(3);
    }

    [Fact]
    public void Current_IsNullOutsideAProjection() {
        ProjectedEvent.Current.Should().BeNull();
    }
}
