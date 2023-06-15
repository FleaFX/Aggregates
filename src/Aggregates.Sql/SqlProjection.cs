using System.Data;

namespace Aggregates.Sql;

/// <summary>
/// Specialization of <see cref="IProjection{TState,TEvent}"/> that provides the implementor with a way to generate SQL queries in response to events being applied.
/// </summary>
/// <typeparam name="TState">The type of the maintained state.</typeparam>
/// <typeparam name="TEvent">The type of the events that are handled.</typeparam>
public abstract record SqlProjection<TState, TEvent>(IDbConnectionFactory DbConnectionFactory) : IProjection<TState, TEvent>
    where TState : SqlProjection<TState, TEvent> {
    /// <summary>
    /// Applies the given <paramref name="event"/> to progress to a new state.
    /// </summary>
    /// <param name="event">The event to apply.</param>
    /// <param name="metadata">A set of metadata that was saved with the event, if any.</param>
    /// <returns>The new state.</returns>
    public abstract ICommit<TState> Apply(TEvent @event, IReadOnlyDictionary<string, object?>? metadata = null);

    /// <summary>
    /// Prepares a <see cref="ICommit{TState}"/> that will execute the given query when committed, along with any queries that were previously prepared.
    /// </summary>
    /// <param name="sql">The command text to execute.</param>
    /// <param name="parameters">Optional object that captures the parameters of the query. Each property on this object should match the name of the parameter to set.</param>
    /// <param name="commandType">The type of command to execute. Defaults to <c>CommandType.Text</c>.</param>
    /// <returns></returns>
    protected ISqlCommit<TState> Query(string sql, object? parameters = null, CommandType commandType = CommandType.Text) =>
        new SqlCommit<TState>((TState)this, DbConnectionFactory).Query(sql, parameters, commandType);

    /// <summary>
    /// Provides an empty commit to use as the seed when you need to fold over a collection to produce a <see cref="ICommit{TState}"/>.
    /// </summary>
    protected ISqlCommit<TState> Seed =>
        new SqlCommit<TState>((TState)this, DbConnectionFactory);
}