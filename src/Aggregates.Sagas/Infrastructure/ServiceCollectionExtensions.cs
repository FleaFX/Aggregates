using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Aggregates.Sagas;

/// <summary>
/// Extension methods for registering <c>Aggregates.Sagas</c> with an
/// <see cref="Microsoft.Extensions.DependencyInjection.IServiceCollection"/>.
/// </summary>
public static class ServiceCollectionExtensions {
    /// <summary>
    /// Adds the <c>Aggregates.Sagas</c> package to the service collection.
    /// Call after <see cref="Aggregates.ServiceCollectionExtensions.AddAggregates"/>.
    /// </summary>
    /// <param name="builder">The aggregates builder returned by <c>AddAggregates</c>.</param>
    /// <param name="configure">
    /// Optional configuration callback. Use <see cref="SagasOptions.ScanAssemblies"/> to
    /// automatically register a handler for every <see cref="ISaga{TSagaState,TEvent}"/>
    /// implementation found in those assemblies.
    /// </param>
    public static ISagasBuilder AddSagas(this IAggregatesBuilder builder, Action<SagasOptions>? configure = null) {
        var options = new SagasOptions();
        configure?.Invoke(options);

        // Decorator chain (open-generic): ISagaHandler<,> → LoggingSagaHandler<,> → RetrySagaHandler<,> → UnitOfWorkAwareSagaHandler<,>
        builder.Services.TryAddScoped(typeof(UnitOfWorkAwareSagaHandler<,>));
        builder.Services.TryAddScoped(typeof(RetrySagaHandler<,>));
        builder.Services.TryAddScoped(typeof(ISagaHandler<,>), typeof(LoggingSagaHandler<,>));

        // ICommandDispatcher implementation
        builder.Services.TryAddScoped<ICommandDispatcher, CommandDispatcher>();

        // Per ISaga<,> implementation: register the saga class and its concrete handler
        foreach (var (sagaType, stateType, eventType) in
            from assembly in options.Assemblies
            from type in assembly.GetTypes()
            where !type.IsAbstract
            from @interface in type.GetInterfaces()
            where @interface.IsGenericType
            where @interface.GetGenericTypeDefinition() == typeof(ISaga<,>)
            let typeArgs = @interface.GetGenericArguments()
            select (sagaType: type, stateType: typeArgs[0], eventType: typeArgs[1])) {

            builder.Services.TryAddScoped(
                typeof(ISaga<,>).MakeGenericType(stateType, eventType),
                sagaType);

            builder.Services.TryAddScoped(
                typeof(SagaHandler<,>).MakeGenericType(stateType, eventType),
                typeof(SagaHandler<,,>).MakeGenericType(sagaType, stateType, eventType));
        }

        // ISagaIdResolver<TEvent> registrations
        foreach (var (eventType, resolver) in options.Resolvers)
            builder.Services.TryAdd(ServiceDescriptor.Singleton(typeof(ISagaIdResolver<>).MakeGenericType(eventType), resolver));

        return new SagasBuilder(builder.Services);
    }
}

internal sealed class SagasBuilder(IServiceCollection services) : ISagasBuilder {
    public IServiceCollection Services => services;
}
