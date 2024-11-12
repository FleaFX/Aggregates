using System.Data;
using Aggregates.Projections;

namespace Aggregates.Sql;

public interface ISqlCommit : ICommit {
    /// <summary>
    /// Prepares a <see cref="ISqlCommit"/> that will execute the given query when committed, along with any queries that were previously prepared.
    /// </summary>
    /// <param name="sql">The command text to execute.</param>
    /// <param name="parameters">Optional object that captures the parameters of the query. Each property on this object should match the name of the parameter to set.</param>
    /// <param name="commandType">The type of command to execute. Defaults to <c>CommandType.Text</c>.</param>
    /// <returns></returns>
    ISqlCommit Query(string sql, object? parameters = null, CommandType commandType = CommandType.Text);
}