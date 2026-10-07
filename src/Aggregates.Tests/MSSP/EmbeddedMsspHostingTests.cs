using Aggregates.Projections;
using Aggregates.Projections.MSSP;
using Aggregates.Testing;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using MsspEngine = global::MSSP.Engine;
using MsspEventData = global::MSSP.EventData;

namespace Aggregates.MSSP;

[Trait("Category", "Integration")]
public class EmbeddedMsspHostingTests(ITestOutputHelper output) {
    [Fact(Skip = "Embedded MSSP registered with services.AddMssp provides no IMsspClient until its hosted service has started, but the subscription services need one when the host constructs them")]
    public async Task ProjectionRuns_WhenEmbeddedStoreIsRegisteredInSameHost() {
        var directory = Path.Combine(Path.GetTempPath(), "aggregates-tests", Guid.NewGuid().ToString("N"));
        var probe = new HandlerProbe();
        var serialization = new SerializationSetup(probe, Orders.EventTypes);

        var builder = Host.CreateApplicationBuilder();
        builder.Logging.ClearProviders();
        builder.Logging.AddProvider(new TestOutputLoggerProvider(output));
        builder.Services.AddSingleton(probe);
        MsspEngine.MsspServiceCollectionExtensions.AddMssp(builder.Services, o => o.DataDirectory = directory);
        builder.Services.AddAggregates(o => o.ScanTypes(typeof(PlaceOrder)))
            .AddMssp(o => {
                o.Serialize = e => new MsspEventData(serialization.TypeName(e.GetType()), serialization.SerializeData(e));
                o.Deserialize = serialization.Deserialize;
            });
        builder.Services.AddProjections(o => o.ScanTypes(typeof(OrderProjection))).AddMssp();

        try {
            using var host = builder.Build();
            await host.StartAsync(TestContext.Current.CancellationToken);
            try {
                await using (var scope = host.Services.CreateAsyncScope())
                    await scope.ServiceProvider.GetRequiredService<ICommandHandler<PlaceOrder>>()
                        .HandleAsync(new PlaceOrder("order-1", "alice"), TestContext.Current.CancellationToken);

                await Eventually.UntilAsync(() => probe.Count<OrderProjection>() == 1, "the projection handles OrderPlaced");
                probe.Events<OrderProjection>().Should().Equal(new OrderPlaced("order-1", "alice"));
            } finally {
                await host.StopAsync(CancellationToken.None);
            }
        } finally {
            if (Directory.Exists(directory))
                Directory.Delete(directory, recursive: true);
        }
    }

    [ProjectionContract("Orders")]
    sealed class OrderProjection(HandlerProbe probe) : IProjection<IOrderEvent> {
        public ValueTask<ICommit> ProjectAsync(IOrderEvent @event, EventMetadata metadata, CancellationToken cancellationToken = default) {
            probe.Record(this, @event, metadata);
            return ValueTask.FromResult(Commit.Create());
        }
    }
}
