using System.ComponentModel;
using Microsoft.Extensions.DependencyInjection;

namespace Aggregates.Subscriptions.Internal;

/// <summary>
/// Helpers for the packages that register subscription handlers (projections, policies and sagas):
/// finding the handler classes, deriving their subscription id and registering them without
/// duplicate ids.
/// </summary>
/// <remarks>
/// Internal infrastructure of the Aggregates packages. Public only so that those packages can use it;
/// it is not covered by semantic versioning and may change in any release.
/// </remarks>
[EditorBrowsable(EditorBrowsableState.Never)]
public static class SubscriptionRegistrations {
    /// <summary>
    /// Finds the handler classes among <paramref name="types"/>: every concrete, closed type that
    /// implements <paramref name="openGenericInterface"/> (for example <c>IProjection&lt;&gt;</c>).
    /// </summary>
    /// <param name="types">The types to inspect. Duplicates are ignored.</param>
    /// <param name="openGenericInterface">The open generic handler interface.</param>
    /// <returns>Each handler class with the closed interface it implements.</returns>
    /// <exception cref="InvalidOperationException">
    /// A type implements <paramref name="openGenericInterface"/> more than once.
    /// </exception>
    public static IReadOnlyList<(Type HandlerType, Type HandlerInterface)> FindHandlers(IEnumerable<Type> types, Type openGenericInterface) {
        var handlers = new List<(Type, Type)>();
        foreach (var type in types.Distinct()) {
            if (type.IsAbstract || type.ContainsGenericParameters)
                continue;

            var interfaces = type.GetInterfaces()
                .Where(@interface => @interface.IsGenericType && @interface.GetGenericTypeDefinition() == openGenericInterface)
                .ToArray();

            switch (interfaces) {
                case []:
                    continue;
                case [var single]:
                    handlers.Add((type, single));
                    break;
                default:
                    throw new InvalidOperationException(
                        $"{type.FullName} implements {string.Join(", ", interfaces.Select(DisplayName))}. " +
                        $"A handler class implements {DisplayName(openGenericInterface)} once: " +
                        "use a marker interface (or object) as the event type to handle several event types.");
            }
        }
        return handlers;
    }

    /// <summary>
    /// Derives the subscription id of <paramref name="handlerType"/>: the string form of its
    /// contract attribute (<c>{namespace}.{name}@v{version}</c>), or the type's full name when it has none.
    /// </summary>
    /// <param name="handlerType">The projection, policy or saga class.</param>
    /// <param name="contract">The contract attribute on <paramref name="handlerType"/>, if any.</param>
    public static string GetSubscriptionId(Type handlerType, Attribute? contract) =>
        contract?.ToString() ?? handlerType.FullName ?? handlerType.Name;

    /// <summary>
    /// Adds <paramref name="registration"/> to <paramref name="services"/> as a singleton instance,
    /// unless the same handler class is already registered under the same id.
    /// </summary>
    /// <param name="services">The service collection.</param>
    /// <param name="registration">The subscription to register.</param>
    /// <returns>
    /// <see langword="true"/> when the registration was added; <see langword="false"/> when it was
    /// already present, in which case the caller registers nothing else for it either.
    /// </returns>
    /// <exception cref="InvalidOperationException">
    /// Another handler class is registered under the same subscription id. Both would share one
    /// checkpoint and one parked-message stream. Also thrown when the subscription continues from
    /// itself, or from a subscription id that is registered in the same host by another handler
    /// class: both versions would process the same events.
    /// </exception>
    public static bool Add(IServiceCollection services, SubscriptionRegistration registration) {
        var existing = services
            .Where(descriptor => descriptor.ServiceType == typeof(SubscriptionRegistration))
            .Select(descriptor => descriptor.ImplementationInstance)
            .OfType<SubscriptionRegistration>()
            .FirstOrDefault(r => string.Equals(r.SubscriptionId, registration.SubscriptionId, StringComparison.Ordinal));

        if (existing is not null) {
            if (existing.HandlerType == registration.HandlerType)
                return false;

            throw new InvalidOperationException(
                $"Subscription id '{registration.SubscriptionId}' is used by both {existing.HandlerType.FullName} " +
                $"and {registration.HandlerType.FullName}. They would share one checkpoint: give one of them " +
                "a different contract name, namespace or version.");
        }

        if (registration.Predecessors?.Contains(registration.SubscriptionId, StringComparer.Ordinal) == true)
            throw new InvalidOperationException(
                $"Subscription '{registration.SubscriptionId}' of {registration.HandlerType.FullName} continues from itself: " +
                "set continueFrom to the id of an older contract, or to \"\" for none.");

        foreach (var other in services
            .Where(descriptor => descriptor.ServiceType == typeof(SubscriptionRegistration))
            .Select(descriptor => descriptor.ImplementationInstance)
            .OfType<SubscriptionRegistration>()) {
            if (registration.Predecessors?.Contains(other.SubscriptionId, StringComparer.Ordinal) == true)
                throw ContinuesFromARegisteredType(registration.SubscriptionId, other.SubscriptionId, other.HandlerType);
            if (other.Predecessors?.Contains(registration.SubscriptionId, StringComparer.Ordinal) == true)
                throw ContinuesFromARegisteredType(other.SubscriptionId, registration.SubscriptionId, registration.HandlerType);
        }

        services.AddSingleton(registration);
        return true;
    }

    // The message of the same-host predecessor check in Add.
    static InvalidOperationException ContinuesFromARegisteredType(string successor, string predecessor, Type handlerType) =>
        new(
            $"'{successor}' continues from '{predecessor}', which is registered in the same host by {handlerType.FullName}. " +
            "Both would process the same events: remove the old version, or set continueFrom: " +
            "ContinueFrom.None on the new one to replay.");

    // IProjection<OrderPlaced> instead of IProjection`1.
    static string DisplayName(Type type) {
        if (!type.IsGenericType)
            return type.Name;
        var arity = type.Name.IndexOf('`');
        var name = arity < 0 ? type.Name : type.Name[..arity];
        return $"{name}<{string.Join(", ", type.GetGenericArguments().Select(DisplayName))}>";
    }
}
