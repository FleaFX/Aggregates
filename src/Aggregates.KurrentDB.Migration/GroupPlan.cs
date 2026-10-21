namespace Aggregates.KurrentDB.Migration;

/// <summary>
/// What the plan does with one 0.x persistent subscription group.
/// </summary>
public sealed record GroupPlan {
    /// <summary>
    /// The name of the group, which is also the 1.0 subscription id.
    /// </summary>
    public required string GroupName { get; init; }

    /// <summary>
    /// What the state of the group says about its position.
    /// </summary>
    public required GroupState State { get; init; }

    /// <summary>
    /// The 0.x position to import: exact when the group is drained, the server's own checkpoint
    /// when it is not, null when there is none.
    /// </summary>
    public ulong? Position { get; init; }

    /// <summary>
    /// The 1.0 checkpoint the step compares or works with: <c>checkpoint-{GroupName}</c> for
    /// Import and Delete; for Rollback, the checkpoint of the 1.0 subscription that continues
    /// from the group.
    /// </summary>
    public ulong? Checkpoint { get; init; }

    /// <summary>
    /// How many parked messages the group holds.
    /// </summary>
    public required long ParkedMessages { get; init; }

    /// <summary>
    /// The group's event-type regular expression from <c>$persistentSubscriptionConfig</c>, or
    /// null when it is not readable.
    /// </summary>
    public string? EventTypeFilter { get; init; }

    /// <summary>
    /// What the step does with the group.
    /// </summary>
    public required GroupAction Action { get; init; }

    /// <summary>
    /// Why the step does what it does, shown in the report.
    /// </summary>
    public required string Reason { get; init; }

    /// <summary>
    /// Whether the step refuses the group; <see cref="MigrationPlan.CanApply"/> is false
    /// while any group is blocked.
    /// </summary>
    public bool IsBlocked => Action == GroupAction.Blocked;
}
