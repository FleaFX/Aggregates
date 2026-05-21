using System.Reflection;

namespace Aggregates;

/// <summary>
/// Configuration options for the Aggregates library. Passed to
/// <see cref="ServiceCollectionExtensions.AddAggregates"/>.
/// </summary>
public sealed class AggregatesOptions {
    internal List<Assembly> Assemblies { get; } = [];

    /// <summary>
    /// Scans <paramref name="assemblies"/> for <see cref="ICommand{TState,TEvent}"/> implementations
    /// and automatically registers a <see cref="CommandHandler{TCommand,TState,TEvent}"/> for each.
    /// </summary>
    public AggregatesOptions ScanAssemblies(params Assembly[] assemblies) {
        Assemblies.AddRange(assemblies);
        return this;
    }
}
