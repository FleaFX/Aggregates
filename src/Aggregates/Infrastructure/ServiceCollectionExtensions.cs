using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Aggregates;

/// <summary>
/// Extension methods for registering the Aggregates library with an <see cref="IServiceCollection"/>.
/// </summary>
public static class ServiceCollectionExtensions {
    /// <summary>
    /// Adds the Aggregates library to the service collection and returns a builder for further
    /// configuration (e.g. registering a storage integration).
    /// </summary>
    /// <param name="services">The service collection to configure.</param>
    /// <param name="configure">
    /// Optional configuration callback. Use <see cref="AggregatesOptions.ScanAssemblies"/> to
    /// automatically register a <see cref="CommandHandler{TCommand,TState,TEvent}"/> for every
    /// <see cref="ICommand{TState,TEvent}"/> implementation found in those assemblies.
    /// </param>
    public static IAggregatesBuilder AddAggregates(this IServiceCollection services, Action<AggregatesOptions>? configure = null) {
        var options = new AggregatesOptions();
        configure?.Invoke(options);

        var builder = new AggregatesBuilder(services);
        builder.Services.TryAddScoped<ICommandDispatcher, CommandDispatcher>();
        builder.Services.TryAddScoped(typeof(UnitOfWorkAwareCommandHandler<>));
        builder.Services.TryAddScoped(typeof(MetadataAwareCommandHandler<>));
        builder.Services.TryAddScoped(typeof(RetryCommandHandler<>));
        builder.Services.TryAddScoped(typeof(ICommandHandler<>), typeof(LoggingCommandHandler<>));

        foreach (var (baseType, implType) in
                 from assembly in options.Assemblies
                 from type in assembly.GetTypes()
                 from @interface in type.GetInterfaces()
                 where @interface.IsGenericType
                 where @interface.GetGenericTypeDefinition() == typeof(ICommand<,>)
                 let typeArgs = @interface.GetGenericArguments()
                 select (
                     baseType: typeof(CommandHandler<>).MakeGenericType(type),
                     implType: typeof(CommandHandler<,,>).MakeGenericType(type, typeArgs[0], typeArgs[1])
                ))
            services.TryAddScoped(baseType, implType);

        return builder;
    }
}

internal sealed class AggregatesBuilder(IServiceCollection services) : IAggregatesBuilder {
    public IServiceCollection Services => services;
}
