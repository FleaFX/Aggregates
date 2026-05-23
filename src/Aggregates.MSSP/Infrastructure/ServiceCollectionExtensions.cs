using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Aggregates.MSSP;

/// <summary>
/// Extension methods for registering <c>Aggregates.MSSP</c> with an
/// <see cref="Microsoft.Extensions.DependencyInjection.IServiceCollection"/>.
/// </summary>
public static class ServiceCollectionExtensions {
    /// <summary>
    /// Registers MSSP as the event store for Aggregates, using the specified options.
    /// </summary>
    /// <param name="builder">The <see cref="IAggregatesBuilder"/> to configure.</param>
    /// <param name="configure">Configuration action for <see cref="MsspOptions"/>.</param>
    /// <returns>An <see cref="IMsspBuilder"/> for further configuration.</returns>
    /// <exception cref="InvalidOperationException">Thrown when <see cref="MsspOptions.Serialize"/> is not configured.</exception>
    public static IMsspBuilder AddMssp(this IAggregatesBuilder builder, Action<MsspOptions> configure) {
        var options = new MsspOptions();
        configure(options);

        if (options.Serialize is null)
            throw new InvalidOperationException($"{nameof(MsspOptions)}.{nameof(MsspOptions.Serialize)} must be configured.");
        if (options.Deserialize is null)
            throw new InvalidOperationException($"{nameof(MsspOptions)}.{nameof(MsspOptions.Deserialize)} must be configured.");

        builder.Services.AddSingleton(options);
        builder.Services.AddSingleton<MsspCommitHandler>();
        builder.Services.AddSingleton<CommitDelegate>(sp => sp.GetRequiredService<MsspCommitHandler>().CommitAsync);
        builder.Services.TryAddScoped(typeof(IRepository<,>), typeof(MsspRepository<,>));

        return new MsspBuilder(builder.Services);
    }
}

/// <summary>
/// Builder for configuring MSSP-specific services.
/// </summary>
sealed class MsspBuilder(IServiceCollection services) : IMsspBuilder {
    /// <inheritdoc />
    public IServiceCollection Services => services;
}
