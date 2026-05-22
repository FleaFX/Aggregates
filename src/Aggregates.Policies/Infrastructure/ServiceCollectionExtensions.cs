using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Aggregates.Policies;

/// <summary>
/// Extension methods for registering <c>Aggregates.Policies</c> with an
/// <see cref="Microsoft.Extensions.DependencyInjection.IServiceCollection"/>.
/// </summary>
public static class ServiceCollectionExtensions {
    /// <summary>
    /// Adds the <c>Aggregates.Policies</c> package to the service collection.
    /// Call after <see cref="Aggregates.ServiceCollectionExtensions.AddAggregates"/>.
    /// </summary>
    /// <param name="builder">The aggregates builder returned by <c>AddAggregates</c>.</param>
    /// <param name="configure">
    /// Optional configuration callback. Use <see cref="PoliciesOptions.ScanAssemblies"/> to
    /// automatically register a handler for every <see cref="IPolicy{TEvent}"/> implementation
    /// found in those assemblies.
    /// </param>
    public static IPoliciesBuilder AddPolicies(this IAggregatesBuilder builder, Action<PoliciesOptions>? configure = null) {
        var options = new PoliciesOptions();
        configure?.Invoke(options);

        // Decorator chain: IPolicyHandler<TEvent> → LoggingPolicyHandler<TEvent>
        builder.Services.TryAddScoped(typeof(IPolicyHandler<>), typeof(LoggingPolicyHandler<>));

        var registeredPolicies = new List<(Type EventType, Type PolicyType)>();

        foreach (var (policyType, eventType) in
            from assembly in options.Assemblies
            from type in assembly.GetTypes()
            where !type.IsAbstract
            from @interface in type.GetInterfaces()
            where @interface.IsGenericType
            where @interface.GetGenericTypeDefinition() == typeof(IPolicy<>)
            let typeArgs = @interface.GetGenericArguments()
            select (policyType: type, eventType: typeArgs[0])) {

            builder.Services.TryAddScoped(
                typeof(IPolicy<>).MakeGenericType(eventType),
                policyType);

            builder.Services.TryAddScoped(
                typeof(PolicyHandler<>).MakeGenericType(eventType),
                typeof(PolicyHandler<,>).MakeGenericType(policyType, eventType));

            registeredPolicies.Add((eventType, policyType));
        }

        return new PoliciesBuilder(builder.Services, registeredPolicies);
    }
}

internal sealed class PoliciesBuilder(
    IServiceCollection services,
    IReadOnlyList<(Type EventType, Type PolicyType)> registeredPolicies) : IPoliciesBuilder {
    public IServiceCollection Services => services;
    public IReadOnlyList<(Type EventType, Type PolicyType)> RegisteredPolicies => registeredPolicies;
}
