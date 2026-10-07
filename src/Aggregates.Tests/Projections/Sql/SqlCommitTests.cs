using Aggregates.Testing;
using FluentAssertions;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.DependencyInjection;

namespace Aggregates.Projections.Sql;

[Trait("Category", "Integration")]
public class SqlCommitTests(SqlServerFixture sqlServer, ITestOutputHelper output) : IClassFixture<SqlServerFixture> {
    const string CreateItems = "CREATE TABLE Items (Id int PRIMARY KEY, Name nvarchar(50) NULL)";
    const string InsertItem = "INSERT INTO Items (Id, Name) VALUES (@Id, @Name)";

    [Fact]
    public async Task CommitAsync_RunsQueriesInOrder_WithParameters() {
        var db = await sqlServer.CreateDatabaseAsync();
        await db.ExecuteAsync(CreateItems);

        await Commit.Create().UseSql(db)
            .Query(InsertItem, new { Id = 1, Name = "a" })
            .Query(InsertItem, new { Id = 2, Name = (string?)null })
            .Query("UPDATE Items SET Name = Name + '!' WHERE Id = @Id", new { Id = 1 })
            .CommitAsync(TestContext.Current.CancellationToken);

        (await db.QueryAsync("SELECT Id, Name FROM Items ORDER BY Id")).Should().BeEquivalentTo(
            [new object?[] { 1, "a!" }, new object?[] { 2, null }],
            o => o.WithStrictOrdering());
    }

    [Fact]
    public async Task CommitAsync_RollsBackEarlierQueries_WhenALaterQueryFails() {
        var db = await sqlServer.CreateDatabaseAsync();
        await db.ExecuteAsync(CreateItems);

        var act = async () => await Commit.Create().UseSql(db)
            .Query(InsertItem, new { Id = 1, Name = "a" })
            .Query("INSERT INTO Missing (Id) VALUES (1)")
            .CommitAsync(TestContext.Current.CancellationToken);

        await act.Should().ThrowAsync<SqlException>();
        (await db.QueryAsync("SELECT Id FROM Items")).Should().BeEmpty();
    }

    [Fact]
    public async Task CommitAsync_CommitsParentBeforeQueries() {
        var db = await sqlServer.CreateDatabaseAsync();
        await db.ExecuteAsync(CreateItems);
        int? rowsSeenByParent = null;
        var parent = Commit.Create().Use(() => new ActionCommit(async () =>
            rowsSeenByParent = (await db.QueryAsync("SELECT Id FROM Items")).Count));

        await parent.UseSql(db)
            .Query(InsertItem, new { Id = 1, Name = "a" })
            .CommitAsync(TestContext.Current.CancellationToken);

        rowsSeenByParent.Should().Be(0);
        (await db.QueryAsync("SELECT Id FROM Items")).Should().HaveCount(1);
    }

    [Fact]
    public async Task CommitAsync_RunsNoQueries_WhenParentFails() {
        var db = await sqlServer.CreateDatabaseAsync();
        await db.ExecuteAsync(CreateItems);
        var parent = Commit.Create().Use(() => new ActionCommit(() => throw new InvalidOperationException("parent fails")));

        var act = async () => await parent.UseSql(db)
            .Query(InsertItem, new { Id = 1, Name = "a" })
            .CommitAsync(TestContext.Current.CancellationToken);

        await act.Should().ThrowAsync<InvalidOperationException>().WithMessage("parent fails");
        (await db.QueryAsync("SELECT Id FROM Items")).Should().BeEmpty();
    }

    [Theory, MemberData(nameof(Stores.All), MemberType = typeof(Stores))]
    public async Task Projection_WritesRowPerEvent(Transport transport) {
        var db = await sqlServer.CreateDatabaseAsync();
        await db.ExecuteAsync("CREATE TABLE Orders (OrderId nvarchar(50) PRIMARY KEY, Customer nvarchar(50) NOT NULL)");
        await using var store = await Stores.StartAsync(transport);
        await using var host = await TestHost.StartAsync(store, output, o => o
            .Events(Orders.EventTypes)
            .Projections(typeof(OrderTableProjection))
            .Services(s => s.AddSingleton<IDbConnectionFactory>(db)));

        await store.AppendAsync(host.Serialization, "order-1", new OrderPlaced("order-1", "alice"));
        await store.AppendAsync(host.Serialization, "order-2", new OrderPlaced("order-2", "bob"));

        await Eventually.UntilAsync(async () => (await db.QueryAsync("SELECT OrderId FROM Orders")).Count == 2, "both orders are projected");
        (await db.QueryAsync("SELECT OrderId, Customer FROM Orders ORDER BY OrderId")).Should().BeEquivalentTo(
            [new object?[] { "order-1", "alice" }, new object?[] { "order-2", "bob" }],
            o => o.WithStrictOrdering());
    }

    sealed class ActionCommit(Func<ValueTask> action) : ICommit {
        public ActionCommit(Action action) : this(() => { action(); return ValueTask.CompletedTask; }) { }

        public ValueTask CommitAsync(CancellationToken cancellationToken = default) => action();
    }

    [ProjectionContract("OrderTable", @namespace: "IntegrationTests")]
    sealed class OrderTableProjection(IDbConnectionFactory connectionFactory) : IProjection<OrderPlaced> {
        public ValueTask<ICommit> ProjectAsync(OrderPlaced @event, EventMetadata metadata, CancellationToken cancellationToken = default) =>
            ValueTask.FromResult<ICommit>(Commit.Create().UseSql(connectionFactory)
                .Query("INSERT INTO Orders (OrderId, Customer) VALUES (@OrderId, @Customer)", new { @event.OrderId, @event.Customer }));
    }
}
