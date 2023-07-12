using Aggregates.Extensions;
using Aggregates.Types;
using Microsoft.Extensions.DependencyInjection;

namespace Aggregates.Configuration;

/// <summary>
/// A configuration object that can be used to complete non-default options for the Aggregates package.
/// </summary>
public class AggregatesOptions {
    /// <summary>
    /// Gets or sets the way command handlers decide how to handle creation of a new aggregate.
    /// </summary>
    /// <remarks>
    /// Use <see cref="AggregateCreationBehaviour.Automatic()" /> or <see cref="AggregateCreationBehaviour.UseMarkerInterface{T}()" /> to configure the desired
    /// command handler behaviour.
    /// </remarks>
    public AggregateCreationBehaviour AggregateCreationBehaviour { get; set; } = AggregateCreationBehaviour.Automatic();

    /// <summary>
    /// Gets or sets the key by which the <see cref="AggregateIdentifier"/> of a saga will be referenced in the metadata of a stored event.
    /// </summary>
    public string SagaIdKey { get; set; } = "SagaId";

    internal Action<IServiceCollection>? ConfigureServices { get; private set; }

    internal void AddConfiguration(Action<IServiceCollection> configuration) =>
        ConfigureServices = ConfigureServices.AndThen(configuration);
}