using Microsoft.Extensions.DependencyInjection;

namespace Aggregates.Configuration;

/// <summary>
/// Configures how aggregates should be created.
/// </summary>
public abstract class AggregateCreationBehaviour {
    /// <summary>
    /// Configures command handlers to attempt to load an existing aggregate to handle the command, or create a new one if the aggregate is not found.
    /// </summary>
    public static AggregateCreationBehaviour Automatic() => new AutomaticAggregateCreationBehaviour();

    /// <summary>
    /// Configures command handlers to use the given <typeparamref name="T"/> to inspect whether commands create new aggregates.
    /// I.e. for commands marked with the given interface type, the command handler will not attempt to load an existing aggregate and go straight through
    /// to creating a new aggregate. Should an aggregate with the same identifier already exist, an exception will be thrown. Conversely, if the command is
    /// not marked with the interface and the aggregate can not be found, a <see cref="AggregateRootNotFoundException"/> will be thrown.
    /// </summary>
    /// <typeparam name="T">The type of the interface that marks creation commands.</typeparam>
    public static AggregateCreationBehaviour UseMarkerInterface<T>() => new MarkerInterfaceCreationBehaviour<T>();

    internal abstract IServiceCollection Configure(IServiceCollection services);
}