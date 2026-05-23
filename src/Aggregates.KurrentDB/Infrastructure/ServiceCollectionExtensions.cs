using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Aggregates.KurrentDB;

/// <summary>
/// Extension methods for registering <c>Aggregates.KurrentDB</c> with an
/// <see cref="Microsoft.Extensions.DependencyInjection.IServiceCollection"/>.
/// </summary>
public static class ServiceCollectionExtensions {
    /// <summary>
    /// Adds the <c>Aggregates.KurrentDB</c> storage integration to the service collection.
    /// Call after <see cref="Aggregates.ServiceCollectionExtensions.AddAggregates"/>.
    /// </summary>
    /// <remarks>
    /// A <c>KurrentDBClient</c> must be registered in the service collection before calling
    /// this method (e.g. via <c>services.AddKurrentDBClient(...)</c>).
    /// </remarks>
    /// <param name="builder">The aggregates builder returned by <c>AddAggregates</c>.</param>
    /// <param name="configure">
    /// Required configuration callback. Use <see cref="KurrentDbOptions.Serialize"/> and
    /// <see cref="KurrentDbOptions.Deserialize"/> to supply serialization delegates.
    /// </param>
    public static IKurrentDbBuilder AddKurrentDb(this IAggregatesBuilder builder, Action<KurrentDbOptions> configure) {
        var options = new KurrentDbOptions();
        configure(options);

        if (options.Serialize is null)
            throw new InvalidOperationException($"{nameof(KurrentDbOptions)}.{nameof(KurrentDbOptions.Serialize)} must be configured.");
        if (options.Deserialize is null)
            throw new InvalidOperationException($"{nameof(KurrentDbOptions)}.{nameof(KurrentDbOptions.Deserialize)} must be configured.");

        builder.Services.AddSingleton(options);
        builder.Services.AddSingleton<KurrentDbCommitHandler>();
        builder.Services.AddSingleton<CommitDelegate>(sp => sp.GetRequiredService<KurrentDbCommitHandler>().CommitAsync);
        builder.Services.TryAddScoped(typeof(IRepository<,>), typeof(KurrentDbRepository<,>));

        return new KurrentDbBuilder(builder.Services);
    }
}

sealed class KurrentDbBuilder(IServiceCollection services) : IKurrentDbBuilder {
    public IServiceCollection Services => services;
}
