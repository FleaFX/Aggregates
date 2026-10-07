using System.Reflection;

namespace Aggregates;

/// <summary>
/// Configuration options for the Aggregates library. Passed to
/// <see cref="ServiceCollectionExtensions.AddAggregates"/>.
/// </summary>
public sealed class AggregatesOptions {
    internal List<Type> Types { get; } = [];

    /// <summary>
    /// Scans <paramref name="assemblies"/> for <see cref="ICommand{TState,TEvent}"/> implementations
    /// and automatically registers a <see cref="CommandHandler{TCommand,TState,TEvent}"/> for each.
    /// </summary>
    public AggregatesOptions ScanAssemblies(params Assembly[] assemblies) {
        foreach (var assembly in assemblies)
            Types.AddRange(assembly.GetTypes());
        return this;
    }

    /// <summary>
    /// Inspects <paramref name="types"/> for <see cref="ICommand{TState,TEvent}"/> implementations
    /// and automatically registers a <see cref="CommandHandler{TCommand,TState,TEvent}"/> for each.
    /// Use this instead of <see cref="ScanAssemblies"/> to register an explicit set of commands.
    /// </summary>
    public AggregatesOptions ScanTypes(params Type[] types) {
        Types.AddRange(types);
        return this;
    }
}
