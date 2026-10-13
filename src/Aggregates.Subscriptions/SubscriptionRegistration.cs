namespace Aggregates.Subscriptions;

/// <summary>
/// Describes one subscription that a hosted service runs: its id, the handler class and the event
/// type it handles. Registered as a singleton instance for every projection, policy and saga, so
/// the registrations of a host can be listed with <c>IEnumerable&lt;SubscriptionRegistration&gt;</c>.
/// </summary>
/// <param name="SubscriptionId">
/// Identifies the subscription's checkpoint (<c>checkpoint-{id}</c>) and parked messages
/// (<c>parked-{id}</c>). Unique within a host.
/// </param>
/// <param name="HandlerType">The projection, policy or saga class.</param>
/// <param name="EventType">The event type the handler class handles.</param>
/// <param name="StartFromEnd">
/// Whether the subscription starts from the end of the store when it has no checkpoint yet.
/// </param>
/// <param name="Predecessors">
/// The subscription ids whose checkpoint this subscription continues from when it has none of its
/// own, nearest first; null or empty when the subscription has no predecessor.
/// </param>
public sealed record SubscriptionRegistration(
    string SubscriptionId,
    Type HandlerType,
    Type EventType,
    bool StartFromEnd,
    IReadOnlyList<string>? Predecessors = null);
