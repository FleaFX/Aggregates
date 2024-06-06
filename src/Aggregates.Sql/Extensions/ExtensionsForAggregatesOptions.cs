// ReSharper disable CheckNamespace

using System.Data;
using Aggregates.Configuration;
using Aggregates.Projections;
using Microsoft.Extensions.DependencyInjection;

namespace Aggregates.Sql;

public static class ExtensionsForAggregatesOptions {
    /// <summary>
    /// Adds configuration to use projections to SQL.
    /// </summary>
    /// <param name="options">The <see cref="AggregatesOptions"/> to configure.</param>
    public static void UseSql(this AggregatesOptions options) {
        options.AddConfiguration(services => {
            // find all implementations of SqlProjection and register them
            foreach (var (implType, stateType, eventType) in
                     from assembly in options.Assemblies ?? AppDomain.CurrentDomain.GetAssemblies()
                     from type in assembly.GetTypes()
                     where !type.IsAbstract && (type.BaseType?.IsGenericType ?? false) && type.BaseType.GetGenericTypeDefinition() == typeof(SqlProjection<,>)

                     let genericArgs = type.BaseType.GetGenericArguments()

                     select (type, genericArgs[0], genericArgs[1])) {
                services.AddScoped(typeof(IProjection<,>).MakeGenericType(stateType, eventType), implType);
            }
        });
    }

    /// <summary>
    /// Creates a <see cref="ISqlCommit{TState}"/> to use when projecting to SQL.
    /// </summary>
    /// <param name="state">The originating state, to be returned after committing the changes.</param>
    /// <param name="dbConnectionFactory">The <see cref="IDbConnectionFactory"/> to use when creating a connection to the database.</param>
    /// <param name="isolationLevel">The transaction locking behaviour to use.</param>
    /// <returns>A <see cref="ISqlCommit{TState}"/>.</returns>
    public static ISqlCommit<TState> UseSql<TState, TEvent>(this Projection<TState, TEvent> state, IDbConnectionFactory dbConnectionFactory, IsolationLevel isolationLevel = IsolationLevel.Unspecified) where TState : Projection<TState, TEvent> =>
        new SqlCommit<TState>((TState)state, dbConnectionFactory, isolationLevel);
}