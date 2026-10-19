using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;

namespace Aggregates.Projections.Sql;

public class RecordSqlPositionsTests {
    [Fact]
    public void RecordSqlPositions_RegistersDefaultTable() {
        var services = new ServiceCollection().AddProjections().RecordSqlPositions().Services;

        services.BuildServiceProvider().GetRequiredService<SqlPositions>().Table.Should().Be("[dbo].[ProjectionPositions]");
    }

    [Fact]
    public void RecordSqlPositions_UsesConfiguredSchemaAndTable() {
        var services = new ServiceCollection().AddProjections()
            .RecordSqlPositions(o => { o.Schema = "projections"; o.Table = "Positions"; }).Services;

        services.BuildServiceProvider().GetRequiredService<SqlPositions>().Table.Should().Be("[projections].[Positions]");
    }

    [Fact]
    public void RecordSqlPositions_EscapesClosingBrackets() {
        var services = new ServiceCollection().AddProjections().RecordSqlPositions(o => o.Table = "a]b").Services;

        services.BuildServiceProvider().GetRequiredService<SqlPositions>().Table.Should().Be("[dbo].[a]]b]");
    }

    [Fact]
    public void RecordSqlPositions_CalledTwice_KeepsTheLast() {
        var builder = new ServiceCollection().AddProjections();
        builder.RecordSqlPositions(o => o.Table = "A");
        builder.RecordSqlPositions(o => o.Table = "B");

        var provider = builder.Services.BuildServiceProvider();
        provider.GetServices<SqlPositions>().Should().HaveCount(1);
        provider.GetRequiredService<SqlPositions>().Table.Should().Be("[dbo].[B]");
    }

    [Theory]
    [InlineData("")]
    [InlineData(" ")]
    public void RecordSqlPositions_WithEmptySchema_Throws(string schema) {
        var act = () => new ServiceCollection().AddProjections().RecordSqlPositions(o => o.Schema = schema);

        act.Should().Throw<ArgumentException>();
    }

    [Theory]
    [InlineData("")]
    [InlineData(" ")]
    public void RecordSqlPositions_WithEmptyTable_Throws(string table) {
        var act = () => new ServiceCollection().AddProjections().RecordSqlPositions(o => o.Table = table);

        act.Should().Throw<ArgumentException>();
    }
}
