namespace Aggregates.KurrentDB.Migration;

/// <summary>
/// What one step of the switch-over will do: per 0.x group the action with its reason, and per
/// 1.0 subscription where it starts. Made by <see cref="PersistentSubscriptionMigration.PlanAsync"/>
/// and shown by <see cref="ToString"/>; applied by
/// <see cref="PersistentSubscriptionMigration.ApplyAsync"/>.
/// </summary>
/// <param name="Step">The step the plan is for.</param>
/// <param name="Options">The options the plan was made with.</param>
/// <param name="Groups">One plan per selected group, in the order the server lists them.</param>
/// <param name="Subscriptions">
/// One row per 1.0 registration, only for the Import step with Registrations; empty otherwise.
/// </param>
public sealed record MigrationPlan(
    MigrationStep Step,
    MigrationOptions Options,
    IReadOnlyList<GroupPlan> Groups,
    IReadOnlyList<SubscriptionPlan> Subscriptions) {
    /// <summary>
    /// Whether <see cref="PersistentSubscriptionMigration.ApplyAsync"/> will execute the plan:
    /// it refuses while any group is blocked.
    /// </summary>
    public bool CanApply => !Groups.Any(group => group.IsBlocked);

    /// <summary>
    /// The dry-run report: the counts and whether the plan can be applied, then the tables of
    /// groups and subscriptions with the blocked groups and warnings first.
    /// </summary>
    public override string ToString() => MigrationReport.Format(this);
}
