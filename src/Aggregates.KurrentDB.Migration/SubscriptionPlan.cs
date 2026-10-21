namespace Aggregates.KurrentDB.Migration;

/// <summary>
/// Where one 1.0 subscription starts after the import, shown per subscription in the plan.
/// </summary>
public sealed record SubscriptionPlan {
    /// <summary>
    /// The id of the 1.0 subscription.
    /// </summary>
    public required string SubscriptionId { get; init; }

    /// <summary>
    /// The projection, policy or saga class.
    /// </summary>
    public required Type HandlerType { get; init; }

    /// <summary>
    /// The position the subscription starts after: its own or an inherited checkpoint, or
    /// "beginning" or "end" when it has none.
    /// </summary>
    public required string StartsAfter { get; init; }

    /// <summary>
    /// Where <see cref="StartsAfter"/> comes from, for example "own checkpoint" or
    /// "0.x group '{id}'".
    /// </summary>
    public required string Source { get; init; }

    /// <summary>
    /// How the event types the subscription receives compare with the ones its 0.x group
    /// received, for example "same (2)" or "+A, -B".
    /// </summary>
    public required string EventTypes { get; init; }

    /// <summary>
    /// Whether the row warns: a blocked source, a subscription that would replay, or a difference
    /// in event types.
    /// </summary>
    public required bool IsWarning { get; init; }
}
