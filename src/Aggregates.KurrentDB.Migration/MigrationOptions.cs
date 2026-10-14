using Aggregates.Subscriptions;

namespace Aggregates.KurrentDB.Migration;

/// <summary>
/// How <see cref="PersistentSubscriptionMigration"/> plans and applies one step: which groups
/// it selects, and how much reprocessing and destruction it may cause.
/// </summary>
public sealed class MigrationOptions {
    /// <summary>
    /// Selects groups by name; null selects every group.
    /// </summary>
    public Func<string, bool>? IncludeGroup { get; init; }

    /// <summary>
    /// The 1.0 subscriptions of the hosts that switch over, so the plan can show where each
    /// one starts and warn about one that would replay.
    /// </summary>
    public IReadOnlyCollection<SubscriptionRegistration>? Registrations { get; init; }

    /// <summary>
    /// Selects only groups whose name is the id or a predecessor of one of the Registrations.
    /// </summary>
    public bool OnlyRegistered { get; init; }

    /// <summary>
    /// The registry of the 1.0 hosts, to compare the event types each subscription receives
    /// with the ones the 0.x group received.
    /// </summary>
    public EventTypeRegistry? EventTypeRegistry { get; init; }

    /// <summary>
    /// Imports a group that is not drained at its own checkpoint: events after it that 0.x
    /// already handled are handled again.
    /// </summary>
    public bool AllowAtLeastOnce { get; init; }

    /// <summary>
    /// Replaces an existing 1.0 checkpoint with a different position.
    /// </summary>
    public bool Overwrite { get; init; }

    /// <summary>
    /// Deletes or recreates groups that hold parked messages, which are lost.
    /// </summary>
    public bool DiscardParkedMessages { get; init; }
}
