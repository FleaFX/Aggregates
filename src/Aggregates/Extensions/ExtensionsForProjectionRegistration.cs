﻿// ReSharper disable CheckNamespace

using System.Reflection;
using Aggregates.Extensions;
using Microsoft.Extensions.DependencyInjection;

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

    internal void AddConfiguration(Action<IServiceCollection> configuration) =>
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
        configure(options);

        options.ConfigureServices?.Invoke(services);

        return services;
    }
}