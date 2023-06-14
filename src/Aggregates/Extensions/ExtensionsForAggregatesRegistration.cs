// ReSharper disable CheckNamespace

using Aggregates.Extensions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Aggregates;

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
    public AggregateCreationBehaviour AggregateCreationBehaviour { get; set; }

    internal Action<IServiceCollection>? ConfigureServices { get; private set; }

    internal void AddConfiguration(Action<IServiceCollection> configuration) =>
        ConfigureServices = ConfigureServices.AndThen(configuration);
}

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

class AutomaticAggregateCreationBehaviour : AggregateCreationBehaviour {
    internal override IServiceCollection Configure(IServiceCollection services) {
        services.TryAddScoped(typeof(GetOrAddHandler<,,>));
        services.TryAddScoped<ICommandHandlerFactory>(sp => new AutomaticCommandHandlerFactory(sp));

        return services;
    }

    class AutomaticCommandHandlerFactory : ICommandHandlerFactory {
        readonly IServiceProvider _serviceProvider;

        public AutomaticCommandHandlerFactory(IServiceProvider serviceProvider) =>
            _serviceProvider = serviceProvider;

        public ICommandHandler<TCommand, TState, TEvent> Create<TCommand, TState, TEvent>()
            where TCommand : ICommand<TCommand, TState, TEvent>
            where TState : IState<TState, TEvent> =>
            _serviceProvider.GetRequiredService<GetOrAddHandler<TCommand, TState, TEvent>>();
    }
}

class MarkerInterfaceCreationBehaviour<TInterface> : AggregateCreationBehaviour {
    internal override IServiceCollection Configure(IServiceCollection services) {
        services.TryAddScoped(typeof(CreationHandler<,,>));
        services.TryAddScoped(typeof(ModificationHandler<,,>));
        services.TryAddScoped(typeof(DefaultHandler<,,>));
        services.TryAddTransient<MarkerInterfaceTypeProviderDelegate>(_ => static () => typeof(TInterface));
        services.TryAddScoped<ICommandHandlerFactory>(sp => new MarkerInterfaceCommandHandlerFactory(sp));
        return services;
    }

    class MarkerInterfaceCommandHandlerFactory : ICommandHandlerFactory {
        readonly IServiceProvider _serviceProvider;

        public MarkerInterfaceCommandHandlerFactory(IServiceProvider serviceProvider) =>
            _serviceProvider = serviceProvider;

        public ICommandHandler<TCommand, TState, TEvent> Create<TCommand, TState, TEvent>() where TCommand : ICommand<TCommand, TState, TEvent> where TState : IState<TState, TEvent> =>
            _serviceProvider.GetRequiredService<DefaultHandler<TCommand, TState, TEvent>>();
    }
}

public static class ExtensionsForAggregatesRegistration {
    /// <summary>
    /// Registers the necessary dependencies to work with the event sourcing infrastructure provided by the Aggregates package.
    /// </summary>
    /// <param name="services">The <see cref="IServiceCollection"/> to register with.</param>
    /// <param name="configure">A function to configure non-default options for the Aggregates package.</param>
    /// <returns>A <see cref="IServiceCollection"/>.</returns>
    public static IServiceCollection UseAggregates(this IServiceCollection services, Action<AggregatesOptions> configure) {
        var options = new AggregatesOptions {
            AggregateCreationBehaviour = AggregateCreationBehaviour.Automatic()
        };
        configure(options);

        options.ConfigureServices?.Invoke(services);

        
        return options.AggregateCreationBehaviour.Configure(services)
            .TryAddUnitOfWork()
            .TryAddMetadata();
    }

    static IServiceCollection TryAddMetadata(this IServiceCollection services) {
        // register as default ICommandHandler implementation
        services.TryAddScoped(typeof(ICommandHandler<,,>), typeof(MetadataAwareHandler<,,>));

        return services;
    }

    static IServiceCollection TryAddUnitOfWork(this IServiceCollection services) {
        services.TryAddScoped<UnitOfWork>();

        services.TryAddScoped(typeof(UnitOfWorkAwareHandler<,,>));

        return services;
    }
}