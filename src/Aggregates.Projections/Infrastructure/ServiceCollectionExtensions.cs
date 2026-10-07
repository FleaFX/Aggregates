using System.Reflection;
using Aggregates.Subscriptions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;

namespace Aggregates.Projections;

/// <summary>
/// Extension methods for registering <c>Aggregates.Projections</c> with an
/// <see cref="Microsoft.Extensions.DependencyInjection.IServiceCollection"/>.
/// </summary>
public static class ServiceCollectionExtensions {
    /// <summary>
    /// Adds the <c>Aggregates.Projections</c> package to the service collection.
    /// </summary>
    /// <param name="services">The service collection.</param>
    /// <param name="configure">
    /// Optional configuration callback. Use <see cref="ProjectionsOptions.ScanAssemblies"/> to
    /// automatically register a handler for every <see cref="IProjection{TEvent}"/> implementation
    /// found in those assemblies.
    /// Use <see cref="ProjectionsOptions.ScanTypes"/> instead to register an explicit set of types.
    /// </param>
    public static IProjectionsBuilder AddProjections(this IServiceCollection services, Action<ProjectionsOptions>? configure = null) {
        var options = new ProjectionsOptions();
        configure?.Invoke(options);

        // Decorator chain: IProjectionHandler<TEvent> → LoggingProjectionHandler<TEvent>
        services.TryAddScoped(typeof(IProjectionHandler<>), typeof(LoggingProjectionHandler<>));

        // Subscription error handling — transport packages register their own IParkedMessageSink;
        // LoggingParkedMessageSink is the fallback for dev/test scenarios without a transport.
        services.TryAddSingleton<IParkedMessageSink, LoggingParkedMessageSink>();
        services.TryAddSingleton(new SubscriptionErrorHandlingOptions());
        services.TryAddSingleton<SubscriptionRetryPolicy>();

        // Subscription checkpointing — positions are written in batches
        services.TryAddSingleton(new SubscriptionCheckpointOptions());
        services.TryAddSingleton(TimeProvider.System);

        // Subscription loop — subscribes again after a transient failure
        services.TryAddSingleton(new SubscriptionResubscribeOptions());
        services.TryAddSingleton<SubscriptionLoop>();

        var registeredProjections = new List<(Type EventType, Type ProjectionType)>();

        foreach (var (projectionType, eventType) in
            from type in options.Types.Distinct()
            where !type.IsAbstract
            from @interface in type.GetInterfaces()
            where @interface.IsGenericType
            where @interface.GetGenericTypeDefinition() == typeof(IProjection<>)
            let typeArgs = @interface.GetGenericArguments()
            select (projectionType: type, eventType: typeArgs[0])) {

            services.TryAddScoped(
                typeof(IProjection<>).MakeGenericType(eventType),
                projectionType);

            services.TryAddScoped(
                typeof(ProjectionHandler<>).MakeGenericType(eventType),
                typeof(ProjectionHandler<,>).MakeGenericType(projectionType, eventType));

            registeredProjections.Add((eventType, projectionType));
        }

        // Subscription hosted service — one per registered projection
        foreach (var (eventType, projectionType) in registeredProjections) {
            var subscriptionId = GetSubscriptionId(projectionType, eventType);
            var startFromEnd = GetStartFromEnd(projectionType);
            var serviceType = typeof(ProjectionSubscriptionService<>).MakeGenericType(eventType);

            services.AddSingleton(typeof(IHostedService), sp =>
                ActivatorUtilities.CreateInstance(sp, serviceType, subscriptionId, startFromEnd));
        }

        return new ProjectionsBuilder(services, registeredProjections);
    }

    static string GetSubscriptionId(Type projectionType, Type eventType) {
        var attr = projectionType.GetCustomAttribute<ProjectionContractAttribute>();
        var contract = attr?.ToString() ?? projectionType.FullName ?? projectionType.Name;
        return $"{contract}-{eventType.Name}";
    }

    static bool GetStartFromEnd(Type projectionType) =>
        projectionType.GetCustomAttribute<ProjectionContractAttribute>()?.StartFromEnd ?? false;
}

internal sealed class ProjectionsBuilder(
    IServiceCollection services,
    IReadOnlyList<(Type EventType, Type ProjectionType)> registeredProjections) : IProjectionsBuilder {
    public IServiceCollection Services => services;
    public IReadOnlyList<(Type EventType, Type ProjectionType)> RegisteredProjections => registeredProjections;
}
