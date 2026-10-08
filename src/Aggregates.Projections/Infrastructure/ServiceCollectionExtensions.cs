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
    /// <remarks>
    /// Every projection class gets one subscription, with the string form of its
    /// <see cref="ProjectionContractAttribute"/> as subscription id (or its full type name when it has none).
    /// </remarks>
    /// <exception cref="InvalidOperationException">
    /// A class implements <see cref="IProjection{TEvent}"/> more than once, or two classes share a
    /// subscription id.
    /// </exception>
    public static IProjectionsBuilder AddProjections(this IServiceCollection services, Action<ProjectionsOptions>? configure = null) {
        var options = new ProjectionsOptions();
        configure?.Invoke(options);

        // Handler chain per projection class: LoggingProjectionHandler<,> → ProjectionHandler<,> → TProjection
        services.TryAddScoped(typeof(LoggingProjectionHandler<,>));
        services.TryAddScoped(typeof(ProjectionHandler<,>));

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

        foreach (var (projectionType, projectionInterface) in SubscriptionRegistrations.FindHandlers(options.Types, typeof(IProjection<>))) {
            var eventType = projectionInterface.GetGenericArguments()[0];
            var contract = projectionType.GetCustomAttribute<ProjectionContractAttribute>();
            var registration = new SubscriptionRegistration(
                SubscriptionRegistrations.GetSubscriptionId(projectionType, contract),
                projectionType,
                eventType,
                contract?.StartFromEnd ?? false);

            if (!SubscriptionRegistrations.Add(services, registration))
                continue;

            services.TryAddScoped(projectionType);

            // Subscription hosted service — one per projection class
            var serviceType = typeof(ProjectionSubscriptionService<,>).MakeGenericType(projectionType, eventType);
            services.AddSingleton(typeof(IHostedService), sp => ActivatorUtilities.CreateInstance(sp, serviceType, registration));

            registeredProjections.Add((eventType, projectionType));
        }

        return new ProjectionsBuilder(services, registeredProjections);
    }
}

internal sealed class ProjectionsBuilder(
    IServiceCollection services,
    IReadOnlyList<(Type EventType, Type ProjectionType)> registeredProjections) : IProjectionsBuilder {
    public IServiceCollection Services => services;
    public IReadOnlyList<(Type EventType, Type ProjectionType)> RegisteredProjections => registeredProjections;
}
