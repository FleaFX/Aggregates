using Aggregates.KurrentDB;
using Aggregates.MSSP;
using Aggregates.Testing;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;

namespace Aggregates;

public class DeserializeUpgradeTests {
    [Fact]
    public void AddKurrentDb_UpgradesWhatDeserializeReturns() {
        var services = new ServiceCollection();
        services.AddAggregates().AddKurrentDb(o => {
            o.Serialize = _ => default;
            o.Deserialize = (_, _) => new OrderCreated("order-1");
        });

        services.BuildServiceProvider().GetRequiredService<KurrentDbOptions>()
            .Deserialize!("x", default).Should().Be(new OrderPlaced("order-1", "unknown"));
    }

    [Fact]
    public void AddMssp_UpgradesWhatDeserializeReturns() {
        var services = new ServiceCollection();
        services.AddAggregates().AddMssp(o => {
            o.Serialize = _ => default!;
            o.Deserialize = (_, _) => new OrderCreated("order-1");
        });

        services.BuildServiceProvider().GetRequiredService<MsspOptions>()
            .Deserialize!("x", default).Should().Be(new OrderPlaced("order-1", "unknown"));
    }
}
