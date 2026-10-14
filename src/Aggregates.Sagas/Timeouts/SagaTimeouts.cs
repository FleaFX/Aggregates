using System.Collections.Concurrent;
using System.Reflection;
using Aggregates.Subscriptions;

namespace Aggregates.Sagas;

/// <summary>
/// Names derived from a saga for its timeouts.
/// </summary>
static class SagaTimeouts {
    static readonly ConcurrentDictionary<Type, string> Owners = new();

    /// <summary>
    /// The owner of the timeouts <paramref name="sagaType"/> requests: its subscription id, as
    /// <see cref="ServiceCollectionExtensions.AddSagas"/> registers it.
    /// </summary>
    /// <param name="sagaType">The saga class.</param>
    public static string OwnerOf(Type sagaType) =>
        Owners.GetOrAdd(sagaType, type => SubscriptionRegistrations.GetSubscriptionId(type, type.GetCustomAttribute<SagaContractAttribute>()));

    /// <summary>
    /// The id of the timeout scheduler of the saga with <paramref name="sagaSubscriptionId"/>: its
    /// checkpoint id, its parked-message id, and its own stream.
    /// </summary>
    /// <param name="sagaSubscriptionId">The subscription id of the saga.</param>
    public static string SubscriptionId(string sagaSubscriptionId) => $"timeouts-{sagaSubscriptionId}";
}
