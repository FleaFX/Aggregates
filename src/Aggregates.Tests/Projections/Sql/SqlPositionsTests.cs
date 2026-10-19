using Aggregates.Subscriptions;
using Aggregates.Testing;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;

namespace Aggregates.Projections.Sql;

[Trait("Category", "Integration")]
public class SqlPositionsTests(SqlServerFixture sqlServer, ITestOutputHelper output) : IClassFixture<SqlServerFixture> {
    const string CreateOrders = "CREATE TABLE Orders (Id int IDENTITY PRIMARY KEY, OrderId nvarchar(50) NOT NULL)";
    const string CreateOutbox = "CREATE TABLE Outbox (Id int IDENTITY PRIMARY KEY, OrderId nvarchar(50) NOT NULL)";
    const string InsertOrder = "INSERT INTO Orders (OrderId) VALUES (@OrderId)";
    const string InsertOutbox = "INSERT INTO Outbox (OrderId) VALUES (@OrderId)";

    [Theory, MemberData(nameof(Stores.All), MemberType = typeof(Stores))]
    public async Task RestartWithoutCheckpoint_WritesEachRowOnce(Transport transport) {
        var db = await sqlServer.CreateDatabaseAsync();
        await db.ExecuteAsync(CreateOrders);
        await db.ExecuteAsync(CreateOutbox);
        await using var store = await Stores.StartAsync(transport);
        var host1 = await TestHost.StartAsync(store, output, o => o
            .Events(Orders.EventTypes)
            .Services(s => s.AddSingleton<IDbConnectionFactory>(db))
            .Projections(typeof(OrderWithOutboxProjection))
            .RecordSqlPositions()
            .Services(s => s.AddSingleton<ICheckpointStore, ForgetfulCheckpointStore>()));
        await store.AppendAsync(host1.Serialization, "order-1", new OrderPlaced("order-1", "alice"));
        await store.AppendAsync(host1.Serialization, "order-2", new OrderPlaced("order-2", "bob"));
        await store.AppendAsync(host1.Serialization, "order-3", new OrderPlaced("order-3", "carol"));
        await Eventually.UntilAsync(async ValueTask<bool> () =>
            (await db.QueryAsync("SELECT OrderId FROM Orders")).Count == 3
            && (await db.QueryAsync("SELECT OrderId FROM Outbox")).Count == 3,
            "the first host has written every row");
        await host1.DisposeAsync();

        await using var host2 = await TestHost.StartAsync(store, output, o => o
            .Events(Orders.EventTypes)
            .Services(s => s.AddSingleton<IDbConnectionFactory>(db))
            .Projections(typeof(OrderWithOutboxProjection))
            .RecordSqlPositions());
        await Eventually.UntilAsync(() => host2.Probe.Count<OrderWithOutboxProjection>() == 3,
            "the restarted host receives the events again");
        await Eventually.QuietAsync();

        (await db.QueryAsync("SELECT OrderId FROM Orders ORDER BY OrderId")).Should().BeEquivalentTo(
            [new object?[] { "order-1" }, new object?[] { "order-2" }, new object?[] { "order-3" }],
            o => o.WithStrictOrdering());
        (await db.QueryAsync("SELECT OrderId FROM Outbox ORDER BY OrderId")).Should().BeEquivalentTo(
            [new object?[] { "order-1" }, new object?[] { "order-2" }, new object?[] { "order-3" }],
            o => o.WithStrictOrdering());
        (await db.QueryAsync("SELECT SubscriptionId, Step FROM dbo.ProjectionPositions")).Should().BeEquivalentTo(
            [new object?[] { "IntegrationTests.OrderWithOutbox@v1", 2 }]);
    }

    [Theory, MemberData(nameof(Stores.All), MemberType = typeof(Stores))]
    public async Task RestartWithoutCheckpoint_WithoutRecordSqlPositions_WritesRowsAgain(Transport transport) {
        var db = await sqlServer.CreateDatabaseAsync();
        await db.ExecuteAsync(CreateOrders);
        await db.ExecuteAsync(CreateOutbox);
        await using var store = await Stores.StartAsync(transport);
        var host1 = await TestHost.StartAsync(store, output, o => o
            .Events(Orders.EventTypes)
            .Services(s => s.AddSingleton<IDbConnectionFactory>(db))
            .Projections(typeof(OrderWithOutboxProjection))
            .Services(s => s.AddSingleton<ICheckpointStore, ForgetfulCheckpointStore>()));
        await store.AppendAsync(host1.Serialization, "order-1", new OrderPlaced("order-1", "alice"));
        await store.AppendAsync(host1.Serialization, "order-2", new OrderPlaced("order-2", "bob"));
        await store.AppendAsync(host1.Serialization, "order-3", new OrderPlaced("order-3", "carol"));
        await Eventually.UntilAsync(async ValueTask<bool> () =>
            (await db.QueryAsync("SELECT OrderId FROM Orders")).Count == 3
            && (await db.QueryAsync("SELECT OrderId FROM Outbox")).Count == 3,
            "the first host has written every row");
        await host1.DisposeAsync();

        await using var host2 = await TestHost.StartAsync(store, output, o => o
            .Events(Orders.EventTypes)
            .Services(s => s.AddSingleton<IDbConnectionFactory>(db))
            .Projections(typeof(OrderWithOutboxProjection)));
        await Eventually.UntilAsync(() => host2.Probe.Count<OrderWithOutboxProjection>() == 3,
            "the restarted host receives the events again");
        await Eventually.QuietAsync();

        (await db.QueryAsync("SELECT OrderId FROM Orders")).Should().HaveCount(6);
        (await db.QueryAsync("SELECT OrderId FROM Outbox")).Should().HaveCount(6);
        (await db.QueryAsync("SELECT OBJECT_ID('dbo.ProjectionPositions')")).Should().BeEquivalentTo([new object?[] { null }]);
    }

    [Theory, MemberData(nameof(Stores.All), MemberType = typeof(Stores))]
    public async Task SecondTransactionFails_WritesEachRowOnceAfterFix(Transport transport) {
        var db = await sqlServer.CreateDatabaseAsync();
        await db.ExecuteAsync(CreateOrders);
        await using var store = await Stores.StartAsync(transport);
        await using var host = await TestHost.StartAsync(store, output, o => o
            .Events(Orders.EventTypes)
            .Services(s => s.AddSingleton<IDbConnectionFactory>(db))
            .Projections(typeof(BlockingOrderWithOutboxProjection))
            .RecordSqlPositions());
        await store.AppendAsync(host.Serialization, "order-1", new OrderPlaced("order-1", "alice"));
        await Eventually.UntilAsync(() => host.Probe.Count<BlockingOrderWithOutboxProjection>() >= 3,
            "the second transaction keeps failing");
        (await db.QueryAsync("SELECT OrderId FROM Orders")).Should().HaveCount(1);

        await db.ExecuteAsync(CreateOutbox);
        await Eventually.UntilAsync(async ValueTask<bool> () =>
            (await db.QueryAsync("SELECT OrderId FROM Outbox")).Count == 1,
            "the outbox row is written once the table exists");
        await Eventually.QuietAsync();

        (await db.QueryAsync("SELECT OrderId FROM Orders")).Should().HaveCount(1);
        (await db.QueryAsync("SELECT OrderId FROM Outbox")).Should().HaveCount(1);
    }

    [Theory, MemberData(nameof(Stores.All), MemberType = typeof(Stores))]
    public async Task ReplayedParkedEvent_IsProjected(Transport transport) {
        var db = await sqlServer.CreateDatabaseAsync();
        await db.ExecuteAsync("CREATE TABLE Orders (Id int IDENTITY PRIMARY KEY, OrderId nvarchar(50) NOT NULL, CONSTRAINT CK_Orders_OrderId CHECK (OrderId <> 'order-1'))");
        await db.ExecuteAsync(CreateOutbox);
        await using var store = await Stores.StartAsync(transport);
        await using var host = await TestHost.StartAsync(store, output, o => o
            .Events(Orders.EventTypes)
            .Services(s => s.AddSingleton<IDbConnectionFactory>(db))
            .Projections(typeof(OrderWithOutboxProjection))
            .RecordSqlPositions());
        var parked = host.Services.GetRequiredService<IParkedMessages>();

        await store.AppendAsync(host.Serialization, "order-1", new OrderPlaced("order-1", "alice"));
        await store.AppendAsync(host.Serialization, "order-2", new OrderPlaced("order-2", "bob"));
        await Eventually.UntilAsync(async ValueTask<bool> () =>
            (await db.QueryAsync("SELECT OrderId FROM Outbox")).Count == 1
            && (await parked.ListAsync("IntegrationTests.OrderWithOutbox@v1", TestContext.Current.CancellationToken)).Count == 1,
            "order-1 is parked and order-2 is projected");
        await db.ExecuteAsync("ALTER TABLE Orders DROP CONSTRAINT CK_Orders_OrderId");

        await parked.ReplayAllAsync("IntegrationTests.OrderWithOutbox@v1", TestContext.Current.CancellationToken);
        await Eventually.UntilAsync(async ValueTask<bool> () =>
            (await db.QueryAsync("SELECT OrderId FROM Orders")).Count == 2,
            "the replayed event is projected");

        (await db.QueryAsync("SELECT OrderId FROM Orders ORDER BY OrderId")).Should().BeEquivalentTo(
            [new object?[] { "order-1" }, new object?[] { "order-2" }], o => o.WithStrictOrdering());
        (await db.QueryAsync("SELECT OrderId FROM Outbox ORDER BY OrderId")).Should().BeEquivalentTo(
            [new object?[] { "order-1" }, new object?[] { "order-2" }], o => o.WithStrictOrdering());
    }

    [Theory, MemberData(nameof(Stores.All), MemberType = typeof(Stores))]
    public async Task ConfiguredSchemaAndTable_AreCreated(Transport transport) {
        var db = await sqlServer.CreateDatabaseAsync();
        await db.ExecuteAsync(CreateOrders);
        await db.ExecuteAsync(CreateOutbox);
        await using var store = await Stores.StartAsync(transport);
        await using var host = await TestHost.StartAsync(store, output, o => o
            .Events(Orders.EventTypes)
            .Services(s => s.AddSingleton<IDbConnectionFactory>(db))
            .Projections(typeof(OrderWithOutboxProjection))
            .RecordSqlPositions(o => { o.Schema = "projections"; o.Table = "Positions"; }));
        await store.AppendAsync(host.Serialization, "order-1", new OrderPlaced("order-1", "alice"));
        await Eventually.UntilAsync(async ValueTask<bool> () =>
            (await db.QueryAsync("SELECT OrderId FROM Outbox")).Count == 1,
            "the order is projected");

        (await db.QueryAsync("SELECT SubscriptionId, Step FROM [projections].[Positions]")).Should().BeEquivalentTo(
            [new object?[] { "IntegrationTests.OrderWithOutbox@v1", 2 }]);
        (await db.QueryAsync("SELECT OBJECT_ID('dbo.ProjectionPositions')")).Should().BeEquivalentTo([new object?[] { null }]);
    }

    sealed class ForgetfulCheckpointStore : ICheckpointStore {
        public ValueTask<ulong?> GetAsync(string subscriptionId, CancellationToken cancellationToken = default) =>
            ValueTask.FromResult<ulong?>(null);

        public ValueTask StoreAsync(string subscriptionId, ulong position, CancellationToken cancellationToken = default) =>
            ValueTask.CompletedTask;
    }

    [ProjectionContract("OrderWithOutbox", @namespace: "IntegrationTests")]
    sealed class OrderWithOutboxProjection(IDbConnectionFactory db, HandlerProbe probe) : IProjection<OrderPlaced> {
        public ValueTask<ICommit> ProjectAsync(OrderPlaced @event, EventMetadata metadata, CancellationToken cancellationToken = default) {
            probe.Record(this, @event);
            return ValueTask.FromResult<ICommit>(Commit.Create()
                .UseSql(db).Query(InsertOrder, new { @event.OrderId })
                .UseSql(db).Query(InsertOutbox, new { @event.OrderId }));
        }
    }

    [ProjectionContract("BlockingOrderWithOutbox", @namespace: "IntegrationTests", onError: SubscriptionErrorMode.Block)]
    sealed class BlockingOrderWithOutboxProjection(IDbConnectionFactory db, HandlerProbe probe) : IProjection<OrderPlaced> {
        public ValueTask<ICommit> ProjectAsync(OrderPlaced @event, EventMetadata metadata, CancellationToken cancellationToken = default) {
            probe.Record(this, @event);
            return ValueTask.FromResult<ICommit>(Commit.Create()
                .UseSql(db).Query(InsertOrder, new { @event.OrderId })
                .UseSql(db).Query(InsertOutbox, new { @event.OrderId }));
        }
    }
}
