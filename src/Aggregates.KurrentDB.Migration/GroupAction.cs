namespace Aggregates.KurrentDB.Migration;

/// <summary>
/// What the plan does with one 0.x group.
/// </summary>
public enum GroupAction {
    /// <summary>
    /// The group is left as it is.
    /// </summary>
    None,

    /// <summary>
    /// Writes <see cref="GroupPlan.Position"/> as the 1.0 checkpoint
    /// <c>checkpoint-{group name}</c>.
    /// </summary>
    WriteCheckpoint,

    /// <summary>
    /// Replaces an existing 1.0 checkpoint with <see cref="GroupPlan.Position"/>.
    /// </summary>
    OverwriteCheckpoint,

    /// <summary>
    /// Deletes the group (and its parked messages, which are lost).
    /// </summary>
    DeleteGroup,

    /// <summary>
    /// Deletes the group and creates it again on <c>$all</c> with the same filter and
    /// settings, starting after the 1.0 checkpoint.
    /// </summary>
    RecreateGroup,

    /// <summary>
    /// The plan refuses the group; <see cref="MigrationPlan.CanApply"/> is false until it is
    /// resolved or left out.
    /// </summary>
    Blocked
}
