namespace Aggregates.KurrentDB.Migration;

/// <summary>
/// Where one 1.0 subscription starts after the import, shown per subscription in the plan.
/// </summary>
/// <param name="SubscriptionId">The id of the 1.0 subscription.</param>
/// <param name="HandlerType">The projection, policy or saga class.</param>
/// <param name="StartsAfter">
/// The position the subscription starts after: its own or an inherited checkpoint, or
/// "beginning" or "end" when it has none.
/// </param>
/// <param name="Source">
/// Where <paramref name="StartsAfter"/> comes from, for example "own checkpoint" or
/// "0.x group '{id}'".
/// </param>
/// <param name="EventTypes">
/// How the event types the subscription receives compare with the ones its 0.x group
/// received, for example "same (2)" or "+A, -B".
/// </param>
/// <param name="IsWarning">
/// Whether the row warns: a blocked source, a subscription that would replay, or a difference
/// in event types.
/// </param>
public sealed record SubscriptionPlan(
    string SubscriptionId,
    Type HandlerType,
    string StartsAfter,
    string Source,
    string EventTypes,
    bool IsWarning);
