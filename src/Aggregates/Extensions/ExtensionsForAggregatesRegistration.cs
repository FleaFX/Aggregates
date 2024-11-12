// ReSharper disable CheckNamespace

using Aggregates.Configuration;
using Aggregates.Entities.Handlers;
using Aggregates.Sagas;
using Aggregates.Sagas.Handlers;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Aggregates;

public static class ExtensionsForAggregatesRegistration {
    /// <summary>
    /// Registers the necessary dependencies to work with the event sourcing infrastructure provided by the Aggregates package.
    /// </summary>
    /// <param name="services">The <see cref="IServiceCollection"/> to register with.</param>
    /// <param name="configure">A function to configure non-default options for the Aggregates package.</param>
    /// <returns>A <see cref="IServiceCollection"/>.</returns>
    public static IServiceCollection UseAggregates(this IServiceCollection services, Action<AggregatesOptions> configure) {
        var options = new AggregatesOptions {
            AggregateCreationBehaviour = AggregateCreationBehaviour.Automatic(),
            SagaKey = "Saga"
        };
        configure(options);

        services.TryAddScoped(typeof(DefaultHandler<,,,,>));
        services.TryAddScoped(typeof(UnitOfWorkAwareHandler<,,,,>));
        services.TryAddScoped(typeof(ISagaHandler<,,,,>), typeof(MetadataAwareHandler<,,,,>));

        services.TryAddSingleton(options);

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