using Aggregates.MSSP.Repositories;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Aggregates.MSSP.Infrastructure;

/// <summary>
/// Extension methods for registering <c>Aggregates.MSSP</c> with an
/// <see cref="Microsoft.Extensions.DependencyInjection.IServiceCollection"/>.
/// </summary>
public static class ServiceCollectionExtensions {
    public static IMsspBuilder AddMssp(this IAggregatesBuilder builder, Action<MsspOptions> configure) {
        var options = new MsspOptions();
        configure(options);

        if (options.Serialize is null)
            throw new InvalidOperationException($"{nameof(MsspOptions)}.{nameof(MsspOptions.Serialize)} must be configured");

        builder.Services.AddSingleton(options);
        builder.Services.AddSingleton<MsspCommitHandler>();
        builder.Services.AddSingleton<CommitDelegate>(sp => sp.GetRequiredService<MsspCommitHandler>().CommitAsync);
        builder.Services.TryAddScoped(typeof(IRepository<,>), typeof(MsspRepository<,>));

        return new MsspBuilder(builder.Services);
    }
}

sealed class MsspBuilder(IServiceCollection services) : IMsspBuilder {
    /// <inheritdoc />
    public IServiceCollection Services => services;
}
