namespace Aggregates.KurrentDB.Migration;

/// <summary>
/// The step of the switch-over that a <see cref="MigrationPlan"/> is made for.
/// </summary>
public enum MigrationStep {
    /// <summary>
    /// Writes each 0.x group's position as the 1.0 checkpoint <c>checkpoint-{group name}</c>,
    /// so the 1.0 subscription continues where the group left off.
    /// </summary>
    Import,

    /// <summary>
    /// Deletes the imported 0.x groups. Only for once 1.0 is accepted: it closes the
    /// rollback window.
    /// </summary>
    Delete,

    /// <summary>
    /// Recreates a 0.x group that still exists at the position where the 1.0 subscription
    /// stopped, so a 0.x host continues where 1.0 left off.
    /// </summary>
    Rollback
}
