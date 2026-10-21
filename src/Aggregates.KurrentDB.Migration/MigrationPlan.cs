namespace Aggregates.KurrentDB.Migration;

/// <summary>
/// What one step of the switch-over will do: per 0.x group the action with its reason, and per
/// 1.0 subscription where it starts. Made by <see cref="PersistentSubscriptionMigration.PlanAsync"/>
/// and shown by <see cref="ToString"/>; applied by
/// <see cref="PersistentSubscriptionMigration.ApplyAsync"/>.
/// </summary>
public sealed record MigrationPlan {
    /// <summary>
    /// The step the plan is for.
    /// </summary>
    public required MigrationStep Step { get; init; }

    /// <summary>
    /// The options the plan was made with.
    /// </summary>
    public required MigrationOptions Options { get; init; }

    /// <summary>
    /// One plan per selected group, in the order the server lists them.
    /// </summary>
    public required IReadOnlyList<GroupPlan> Groups { get; init; }

    /// <summary>
    /// One row per 1.0 registration, only for the Import step with Registrations; empty otherwise.
    /// </summary>
    public required IReadOnlyList<SubscriptionPlan> Subscriptions { get; init; }

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
