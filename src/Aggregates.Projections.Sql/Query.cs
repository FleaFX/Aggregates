using System.Data;

namespace Aggregates.Projections.Sql;

/// <summary>
/// Represents a single SQL statement with its parameters and command type.
/// </summary>
readonly record struct Query(string Sql, object? Parameters, CommandType CommandType);
