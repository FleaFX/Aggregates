using Aggregates.Policies;
using Aggregates.Policies.MSSP;
using Aggregates.Projections;
using Aggregates.Projections.MSSP;
using Aggregates.Sagas;
using Aggregates.Sagas.MSSP;
using Aggregates.Testing;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using MsspEngine = global::MSSP.Engine;
using MsspEventData = global::MSSP.EventData;

namespace Aggregates.MSSP;

/// <summary>
/// Integration tests for an embedded MSSP store registered in the same host as a projection,
/// policy or saga, in both registration orders.
/// </summary>
[Trait("Category", "Integration")]
public class EmbeddedMsspHostingTests(ITestOutputHelper output) {
    [Theory]
    [InlineData(HandlerKind.Projection, true)]
    [InlineData(HandlerKind.Projection, false)]
    [InlineData(HandlerKind.Policy, true)]
    [InlineData(HandlerKind.Policy, false)]
    [InlineData(HandlerKind.Saga, true)]
    [InlineData(HandlerKind.Saga, false)]
    public async Task HandlerRuns_WhenEmbeddedStoreIsRegisteredInSameHost(HandlerKind kind, bool storeRegisteredFirst) {
        var directory = Path.Combine(Path.GetTempPath(), "aggregates-tests", Guid.NewGuid().ToString("N"));
        var probe = new HandlerProbe();
        var handler = ScenarioHandlers.Probe(kind);
        var serialization = new SerializationSetup(probe, Orders.EventTypes);

        var builder = Host.CreateApplicationBuilder();
        builder.Logging.ClearProviders();
        builder.Logging.AddProvider(new TestOutputLoggerProvider(output));
        builder.Services.AddSingleton(probe);
        if (storeRegisteredFirst)
            MsspEngine.MsspServiceCollectionExtensions.AddMssp(builder.Services, o => o.DataDirectory = directory);
        var aggregates = builder.Services.AddAggregates(o => o.ScanTypes(typeof(PlaceOrder)));
        aggregates.AddMssp(o => {
            o.Serialize = e => new MsspEventData(serialization.TypeName(e.GetType()), serialization.SerializeData(e));
            o.Deserialize = serialization.Deserialize;
        });
        switch (kind) {
            case HandlerKind.Projection:
                builder.Services.AddProjections(o => o.ScanTypes(handler)).AddMssp();
                break;
            case HandlerKind.Policy:
                aggregates.AddPolicies(o => o.ScanTypes(handler)).AddMssp();
                break;
            default:
                // Like ScenarioHandlers' own registration: a saga that resolves to the aggregate's
                // own stream parks the event, because the aggregate stream is not a saga stream.
                aggregates.AddSagas(o => o
                    .ScanTypes(handler)
                    .WithResolver<IOrderEvent>(e => [new AggregateIdentifier($"saga-{e.OrderId}")]))
                    .AddMssp();
                break;
        }
        if (!storeRegisteredFirst)
            MsspEngine.MsspServiceCollectionExtensions.AddMssp(builder.Services, o => o.DataDirectory = directory);

        try {
            using var host = builder.Build();
            await host.StartAsync(TestContext.Current.CancellationToken);
            try {
                await using (var scope = host.Services.CreateAsyncScope())
                    await scope.ServiceProvider.GetRequiredService<ICommandHandler<PlaceOrder>>()
                        .HandleAsync(new PlaceOrder("order-1", "alice"), TestContext.Current.CancellationToken);

                await Eventually.UntilAsync(() => probe.Count(handler) == 1, "the handler handles OrderPlaced");
                probe.Events(handler).Should().Equal(new OrderPlaced("order-1", "alice"));
            } finally {
                await host.StopAsync(CancellationToken.None);
            }
        } finally {
            if (Directory.Exists(directory))
                Directory.Delete(directory, recursive: true);
        }
    }
}
