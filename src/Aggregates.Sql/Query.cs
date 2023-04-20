using System.Data;

namespace Aggregates.Sql;

/// <summary>
/// Captures both the command text, type and parameters of a SQL query.
/// </summary>
/// <param name="Sql">The command text to execute.</param>
/// <param name="Parameters">Optional object that captures the parameters of the query. Each property on this object should match the name of the parameter to set.</param>
/// <param name="CommandType">The type of command to execute. Defaults to <c>CommandType.Text</c>.</param>
record struct Query(string Sql, object? Parameters = null, CommandType CommandType = CommandType.Text);