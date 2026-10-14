namespace Aggregates.KurrentDB.Migration;

/// <summary>
/// What the plan does with one 0.x persistent subscription group.
/// </summary>
/// <param name="GroupName">The name of the group, which is also the 1.0 subscription id.</param>
/// <param name="State">What the state of the group says about its position.</param>
/// <param name="Position">
/// The 0.x position to import: exact when the group is drained, the server's own checkpoint
/// when it is not, null when there is none.
/// </param>
/// <param name="Checkpoint">
/// The 1.0 checkpoint the step compares or works with: <c>checkpoint-{GroupName}</c> for
/// Import and Delete; for Rollback, the checkpoint of the 1.0 subscription that continues
/// from the group.
/// </param>
/// <param name="ParkedMessages">How many parked messages the group holds.</param>
/// <param name="EventTypeFilter">
/// The group's event-type regular expression from <c>$persistentSubscriptionConfig</c>, or
/// null when it is not readable.
/// </param>
/// <param name="Action">What the step does with the group.</param>
/// <param name="Reason">Why the step does what it does, shown in the report.</param>
public sealed record GroupPlan(
    string GroupName,
    GroupState State,
    ulong? Position,
    ulong? Checkpoint,
    long ParkedMessages,
    string? EventTypeFilter,
    GroupAction Action,
    string Reason) {
    /// <summary>
    /// Whether the step refuses the group; <see cref="MigrationPlan.CanApply"/> is false
    /// while any group is blocked.
    /// </summary>
    public bool IsBlocked => Action == GroupAction.Blocked;
}
