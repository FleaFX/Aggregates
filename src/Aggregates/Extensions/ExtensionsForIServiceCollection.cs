// ReSharper disable CheckNamespace

using Aggregates.Extensions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Aggregates;

/// <summary>
/// A configuration object that can be used to complete non-default options for the Aggregates package.
/// </summary>
public class AggregatesOptions {
    internal Action<IServiceCollection>? ConfigureServices { get; private set; }

    internal void AddConfiguration(Action<IServiceCollection> configuration) =>
        ConfigureServices = ConfigureServices.AndThen(configuration);
}

public static class ExtensionsForIServiceCollection {
    /// <summary>
    /// Registers the necessary dependencies to work with the event sourcing infrastructure provided by the Aggregates package.
    /// </summary>
    /// <param name="services">The <see cref="IServiceCollection"/> to register with.</param>
    /// <param name="configure">A function to configure non-default options for the Aggregates package.</param>
    /// <returns>A <see cref="IServiceCollection"/>.</returns>
    public static IServiceCollection UseAggregates(this IServiceCollection services, Action<AggregatesOptions> configure) {
        var options = new AggregatesOptions();
        configure(options);

        options.ConfigureServices?.Invoke(services);

        return services
            .TryAddUnitOfWork()
            .TryAddCommandHandlers();
    }

    static IServiceCollection TryAddUnitOfWork(this IServiceCollection services) {
        services.TryAddScoped<UnitOfWork>();

        // register as default ICommandHandler implementation
        services.TryAddScoped(typeof(ICommandHandler<,,>), typeof(UnitOfWorkAwareHandler<,,>));

        return services;
    }

    static IServiceCollection TryAddCommandHandlers(this IServiceCollection services) {
        services.TryAddScoped(typeof(CreationHandler<,,>));
        services.TryAddScoped(typeof(ModificationHandler<,,>));
        services.TryAddScoped(typeof(DefaultHandler<,,>));

        return services;
    }
}