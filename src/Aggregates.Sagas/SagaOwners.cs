using System.Collections.Concurrent;
using System.Reflection;
using Aggregates.Subscriptions;
using Aggregates.Subscriptions.Internal;

namespace Aggregates.Sagas;

/// <summary>
/// The owner of the commands a saga defers.
/// </summary>
static class SagaOwners {
    static readonly ConcurrentDictionary<Type, string> Owners = new();

    /// <summary>
    /// The owner of the commands <paramref name="sagaType"/> defers: its subscription id, as
    /// <see cref="ServiceCollectionExtensions.AddSagas"/> registers it.
    /// </summary>
    /// <param name="sagaType">The saga class.</param>
    public static string Of(Type sagaType) =>
        Owners.GetOrAdd(sagaType, type => SubscriptionRegistrations.GetSubscriptionId(type, type.GetCustomAttribute<SagaContractAttribute>()));
}
