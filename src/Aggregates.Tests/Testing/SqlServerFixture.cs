using System.Data.Common;
using Aggregates.Projections.Sql;
using Microsoft.Data.SqlClient;
using Testcontainers.MsSql;

namespace Aggregates.Testing;

/// <summary>
/// One SQL Server container per test class (<c>IClassFixture</c>): the image is heavy to start.
/// Tests isolate themselves with <see cref="CreateDatabaseAsync"/>.
/// </summary>
public sealed class SqlServerFixture : IAsyncLifetime {
    readonly MsSqlContainer _container = new MsSqlBuilder(TestImages.SqlServer).Build();

    /// <inheritdoc/>
    public async ValueTask InitializeAsync() => await _container.StartAsync();

    /// <inheritdoc/>
    public async ValueTask DisposeAsync() => await _container.DisposeAsync();

    /// <summary>
    /// Creates a fresh, empty database for a single test.
    /// </summary>
    public async Task<SqlDatabase> CreateDatabaseAsync() {
        var name = $"t_{Guid.NewGuid():N}";
        await using (var connection = new SqlConnection(_container.GetConnectionString())) {
            await connection.OpenAsync(TestContext.Current.CancellationToken);
            await using var command = new SqlCommand($"CREATE DATABASE [{name}]", connection);
            await command.ExecuteNonQueryAsync(TestContext.Current.CancellationToken);
        }
        return new SqlDatabase(new SqlConnectionStringBuilder(_container.GetConnectionString()) { InitialCatalog = name }.ConnectionString);
    }
}

/// <summary>
/// A test database: the <see cref="IDbConnectionFactory"/> under test, plus helpers to set up
/// and inspect its contents.
/// </summary>
public sealed class SqlDatabase(string connectionString) : IDbConnectionFactory {
    /// <inheritdoc/>
    public ValueTask<DbConnection> CreateAsync(CancellationToken cancellationToken = default) =>
        ValueTask.FromResult<DbConnection>(new SqlConnection(connectionString));

    /// <summary>
    /// Executes <paramref name="sql"/> outside of any commit under test.
    /// </summary>
    public async Task ExecuteAsync(string sql) {
        await using var connection = new SqlConnection(connectionString);
        await connection.OpenAsync(TestContext.Current.CancellationToken);
        await using var command = new SqlCommand(sql, connection);
        await command.ExecuteNonQueryAsync(TestContext.Current.CancellationToken);
    }

    /// <summary>
    /// Returns the rows of <paramref name="sql"/>, with <see cref="DBNull"/> mapped to <see langword="null"/>.
    /// </summary>
    public async Task<IReadOnlyList<object?[]>> QueryAsync(string sql) {
        await using var connection = new SqlConnection(connectionString);
        await connection.OpenAsync(TestContext.Current.CancellationToken);
        await using var command = new SqlCommand(sql, connection);
        await using var reader = await command.ExecuteReaderAsync(TestContext.Current.CancellationToken);
        var rows = new List<object?[]>();
        while (await reader.ReadAsync(TestContext.Current.CancellationToken)) {
            var row = new object?[reader.FieldCount];
            for (var i = 0; i < row.Length; i++)
                row[i] = reader.IsDBNull(i) ? null : reader.GetValue(i);
            rows.Add(row);
        }
        return rows;
    }
}
