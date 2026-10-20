using FluentAssertions;
using Microsoft.Extensions.Configuration;

namespace Aggregates.Subscriptions;

public class SubscriptionCheckpointOptionsTests {
    [Fact]
    public void HasDocumentedDefaults() {
        var options = new SubscriptionCheckpointOptions();

        options.MaxBatchSize.Should().Be(100);
        options.MaxInterval.Should().Be(TimeSpan.FromSeconds(5));
        options.MaxFilteredEvents.Should().Be(4096);
        options.RequireCheckpoint.Should().BeFalse();
        options.AllowedWithoutCheckpoint.Should().BeEmpty();
    }

    [Fact]
    public void BindsFromConfiguration() {
        var configuration = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?> {
            ["RequireCheckpoint"] = "true",
            ["AllowedWithoutCheckpoint:0"] = "Shop.Invoices@v1",
            ["AllowedWithoutCheckpoint:1"] = "Shop.Mails@v2",
        }).Build();

        var options = configuration.Get<SubscriptionCheckpointOptions>();

        options!.RequireCheckpoint.Should().BeTrue();
        options.AllowedWithoutCheckpoint.Should().Equal("Shop.Invoices@v1", "Shop.Mails@v2");
        options.MaxBatchSize.Should().Be(100);
    }
}
