// ReSharper disable CheckNamespace

using Aggregates.Extensions;
using Microsoft.Extensions.DependencyInjection;
using System.Reflection;
using Aggregates.Projections;

namespace Aggregates;

/// <summary>
/// A configuration object that can be used to complete non-default options regarding projections.
/// </summary>
public class ProjectionsOptions {
    /// <summary>
    /// Gets or sets the set of <see cref="Assembly"/> to scan for projection types.
    /// </summary>
    public Assembly[]? Assemblies { get; set; }

    internal Action<IServiceCollection>? ConfigureServices { get; private set; }

    public void AddConfiguration(Action<IServiceCollection> configuration) =>
        ConfigureServices = ConfigureServices.AndThen(configuration);
}

public static class ExtensionsForProjectionRegistration {
    /// <summary>
    /// Registers the necessary dependencies to work with the projection infrastructure provided by the Aggregates package.
    /// </summary>
    /// <param name="services">The <see cref="IServiceCollection"/> to register with.</param>
    /// <param name="configure">A function to configure non-default options for the Aggregates package.</param>
    /// <returns>A <see cref="IServiceCollection"/>.</returns>
    public static IServiceCollection UseProjections(this IServiceCollection services, Action<ProjectionsOptions> configure) {
        var options = new ProjectionsOptions();
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

        configure(options);

        options.ConfigureServices?.Invoke(services);

        return services;
    }
}