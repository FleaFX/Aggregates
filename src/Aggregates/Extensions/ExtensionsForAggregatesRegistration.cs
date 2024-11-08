// ReSharper disable CheckNamespace

using Aggregates.Configuration;
using Aggregates.Entities.Handlers;
using Aggregates.Projections;
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

        options
            .UseProjections()
            .UseReactions();

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

    /// <summary>
    /// Registers the necessary dependencies to work with the projection infrastructure provided by the Aggregates package.
    /// </summary>
    /// <param name="options">The <see cref="AggregatesOptions"/>> to use when adding projections.</param>
    /// <returns>A <see cref="IServiceCollection"/>.</returns>
    static AggregatesOptions UseProjections(this AggregatesOptions options) =>
        options.AddConfiguration(svc => {
            foreach (var (implType, stateType, eventType) in
                     from assembly in options.Assemblies ?? AppDomain.CurrentDomain.GetAssemblies()
                     where !(assembly.GetName().Name?.Contains("Microsoft.Data.SqlClient") ?? false)
                     from type in assembly.GetTypes()
                     where !type.IsAbstract && (type.BaseType?.IsGenericType ?? false) && type.BaseType.GetGenericTypeDefinition() == typeof(Projection<,>)

                     let genericArgs = type.BaseType.GetGenericArguments()

                     select (type, genericArgs[0], genericArgs[1])) {
                svc.AddScoped(typeof(IProjection<,>).MakeGenericType(stateType, eventType), implType);
            }
        });

    /// <summary>
    /// Registers the necessary dependencies to work with the reaction infrastructure provided by the Aggregates package.
    /// </summary>
    /// <param name="options">The <see cref="AggregatesOptions"/>> to use when adding reactions.</param>
    /// <returns>A <see cref="IServiceCollection"/>.</returns>
    static AggregatesOptions UseReactions(this AggregatesOptions options) =>
        options
            .AddConfiguration(svc => {
                // find all (simple) implementations of IReaction and register them
                foreach (var (implType, reactionEventType, commandType, stateType, eventType) in
                         from assembly in options.Assemblies ?? AppDomain.CurrentDomain.GetAssemblies()
                         where !(assembly.GetName().Name?.Contains("Microsoft.Data.SqlClient") ?? false)
                         from type in assembly.GetTypes()

                         from @interface in type.GetInterfaces()
                         where @interface.IsGenericType && @interface.GetGenericTypeDefinition() == typeof(IReaction<,,,>)

                         let genericArgs = @interface.GetGenericArguments()

                         select (type, genericArgs[0], genericArgs[1], genericArgs[2], genericArgs[3])) {
                    svc.AddScoped(typeof(IReaction<,,,>).MakeGenericType(reactionEventType, commandType, stateType, eventType), implType);
                }
            })
            .AddConfiguration(svc => {
                svc.TryAddScoped(typeof(DefaultHandler<,,,,>));
                svc.TryAddScoped(typeof(UnitOfWorkAwareHandler<,,,,>));
                svc.TryAddScoped(typeof(ISagaHandler<,,,,>), typeof(MetadataAwareHandler<,,,,>));
            });
}