using System.Globalization;
using Aggregates.Subscriptions;

namespace Aggregates.KurrentDB.Migration;

/// <summary>
/// The pure decisions of the migration: which position a 0.x group yields, and what each step
/// does with it. No I/O; everything else is passed in.
/// </summary>
static class MigrationRules {
    /// <summary>
    /// Assesses one group: what its state says about its position. Drained (no consumer,
    /// nothing buffered or in flight) yields the last event read, or the server's checkpoint
    /// when that is unknown, exact because every event the group read was acknowledged. Not drained, the
    /// server's own checkpoint is the safe lower bound. A connected group has no position:
    /// stop the 0.x host first.
    /// </summary>
    /// <param name="group">The facts of the group.</param>
    public static (GroupState State, ulong? Position) Assess(GroupSnapshot group) {
        if (group.EventSource != "$all")
            return (GroupState.NotOnAll, null);

        if (group.Connections > 0)
            return (GroupState.Connected, null);

        if (group.IsDrained) {
            // The last event read, acknowledged since the group is drained. The server's own
            // checkpoint moves on by itself over events the filter skips (spike v1-07 S16), so it
            // only stands in when the last event read is unknown after a server restart.
            if ((group.LastKnownPosition ?? group.LastCheckpointedPosition) is { } drained)
                return (GroupState.Exact, drained);
            return (GroupState.Empty, null);
        }

        if (group.LastCheckpointedPosition is { } checkpointed)
            return (GroupState.AtLeastOnce, checkpointed);

        return (GroupState.Unknown, null);
    }

    /// <summary>
    /// Decides what the Import step does with one group: not on $all is left alone; a connected
    /// group is blocked; an equal existing checkpoint means it was imported already; a different
    /// one is kept unless Overwrite is set; a drained group is imported exactly; a group that is
    /// not drained is imported at its own checkpoint only with AllowAtLeastOnce; a group without
    /// a checkpoint and not drained is blocked; a group that read nothing is left alone.
    /// </summary>
    /// <param name="group">The facts of the group.</param>
    /// <param name="state">The state of the group, from <see cref="Assess"/>.</param>
    /// <param name="position">The position of the group, from <see cref="Assess"/>.</param>
    /// <param name="checkpoint">The existing 1.0 checkpoint <c>checkpoint-{GroupName}</c>, if any.</param>
    /// <param name="options">How much the import may do.</param>
    public static (GroupAction Action, string Reason) DecideImport(
        GroupSnapshot group, GroupState state, ulong? position, ulong? checkpoint, MigrationOptions options) {
        if (state == GroupState.NotOnAll)
            return (GroupAction.None, "Not a group on $all; Aggregates 0.x only created groups on $all.");

        if (state == GroupState.Connected)
            return (GroupAction.Blocked, group.IsDrained
                ? $"{group.Connections} consumer(s) connected, caught up: stop the 0.x host first."
                : $"{group.Connections} consumer(s) connected, not caught up: stop the writers and let the group catch up, then stop the 0.x host.");

        if (checkpoint is { } existing) {
            if (existing == position)
                return (GroupAction.None, "Already imported.");
            if (!options.Overwrite)
                return (GroupAction.None, $"checkpoint-{group.GroupName} exists at {existing}: a 1.0 subscription ran already, or the group was imported before. Set Overwrite to replace it.");
        }

        return state switch {
            GroupState.Exact => (Write(checkpoint, options), "Drained: every event the group read was acknowledged."),
            GroupState.AtLeastOnce when options.AllowAtLeastOnce =>
                (Write(checkpoint, options), "Not drained: imported at the 0.x checkpoint, so events after it that 0.x already handled are handled again."),
            GroupState.AtLeastOnce => (GroupAction.Blocked, "Not drained (unacknowledged or unread events): let the 0.x host catch up and stay idle for a few seconds before stopping it, or set AllowAtLeastOnce to import at the 0.x checkpoint."),
            GroupState.Unknown => (GroupAction.Blocked, "No 0.x checkpoint and not drained: run the 0.x host until the group has caught up, then stop it."),
            _ => (GroupAction.None, "The group has read no events: the 1.0 subscription starts as its contract says."),
        };
    }

    /// <summary>
    /// Decides what the Delete step does with one group: not on $all is left alone; a connected
    /// group is blocked; a group without a 1.0 checkpoint is blocked because its position would
    /// be lost; parked messages block the deletion unless DiscardParkedMessages is set.
    /// </summary>
    /// <param name="group">The facts of the group.</param>
    /// <param name="checkpoint">The 1.0 checkpoint <c>checkpoint-{GroupName}</c>, which must exist.</param>
    /// <param name="options">How much the deletion may do.</param>
    public static (GroupAction Action, string Reason) DecideDelete(GroupSnapshot group, ulong? checkpoint, MigrationOptions options) {
        if (group.EventSource != "$all")
            return (GroupAction.None, "Not a group on $all; Aggregates 0.x only created groups on $all.");

        if (group.Connections > 0)
            return (GroupAction.Blocked, group.IsDrained
                ? $"{group.Connections} consumer(s) connected, caught up: stop the 0.x host first."
                : $"{group.Connections} consumer(s) connected, not caught up: stop the writers and let the group catch up, then stop the 0.x host.");

        if (checkpoint is null)
            return (GroupAction.Blocked, $"No checkpoint-{group.GroupName}: import the group first, or its position is lost.");

        if (group.ParkedMessages > 0 && !options.DiscardParkedMessages)
            return (GroupAction.Blocked, $"{group.ParkedMessages} parked message(s) are deleted with the group: replay them in 0.x first, or set DiscardParkedMessages.");

        return (GroupAction.DeleteGroup, group.ParkedMessages > 0
            ? $"Imported; deletes {group.ParkedMessages} parked message(s)."
            : "Imported.");
    }

    /// <summary>
    /// Decides what the Rollback step does with one group: not on $all is left alone; a connected
    /// group is blocked; without a 1.0 checkpoint the group is left as it is; an unknown filter
    /// blocks the rollback because the group cannot be created again; parked messages block it
    /// unless DiscardParkedMessages is set; otherwise the group is created again after the 1.0
    /// checkpoint.
    /// </summary>
    /// <param name="group">The facts of the group.</param>
    /// <param name="checkpoint">The 1.0 checkpoint the group continues from.</param>
    /// <param name="filter">The group's event-type regular expression, or null when unknown.</param>
    /// <param name="options">How much the rollback may do.</param>
    public static (GroupAction Action, string Reason) DecideRollback(GroupSnapshot group, ulong? checkpoint, string? filter, MigrationOptions options) {
        if (group.EventSource != "$all")
            return (GroupAction.None, "Not a group on $all; Aggregates 0.x only created groups on $all.");

        if (group.Connections > 0)
            return (GroupAction.Blocked, group.IsDrained
                ? $"{group.Connections} consumer(s) connected, caught up: stop the 0.x host first."
                : $"{group.Connections} consumer(s) connected, not caught up: stop the writers and let the group catch up, then stop the 0.x host.");

        if (checkpoint is null)
            return (GroupAction.None, "No 1.0 checkpoint for this group: it is left as it is.");

        if (filter is null)
            return (GroupAction.Blocked, "The group's event-type filter is unknown ($persistentSubscriptionConfig is not readable, or the filter is not an event-type regex): the group can't be created again.");

        if (group.ParkedMessages > 0 && !options.DiscardParkedMessages)
            return (GroupAction.Blocked, $"{group.ParkedMessages} parked message(s) are deleted when the group is created again: replay them in 0.x first, or set DiscardParkedMessages.");

        return (GroupAction.RecreateGroup, $"Created again after the 1.0 checkpoint {checkpoint}.");
    }

    /// <summary>
    /// The names of the groups that make <paramref name="plan"/> stale against
    /// <paramref name="current"/>: groups that appeared or disappeared, or whose state,
    /// position, checkpoint or action changed. A position that moved while the group stayed
    /// <see cref="GroupState.Exact"/> doesn't count: the current position is exact as well, and
    /// it is the one that is written. Delete and Rollback don't use the group's own state and
    /// position, which keep moving while the group is idle, so only the checkpoint and the
    /// action count there.
    /// </summary>
    /// <param name="plan">The plan to execute.</param>
    /// <param name="current">The same plan, made again against the store.</param>
    public static List<string> ChangedGroups(MigrationPlan plan, MigrationPlan current) {
        var before = plan.Groups.ToDictionary(group => group.GroupName, StringComparer.Ordinal);
        var after = current.Groups.ToDictionary(group => group.GroupName, StringComparer.Ordinal);
        var import = plan.Step == MigrationStep.Import;
        var changed = new List<string>(after.Keys.Where(name => !before.ContainsKey(name)));
        foreach (var (name, group) in before) {
            if (!after.TryGetValue(name, out var other)
                || group.Checkpoint != other.Checkpoint || group.Action != other.Action
                || (import && group.State != other.State)
                || (import && group.Position != other.Position && group.State != GroupState.Exact))
                changed.Add(name);
        }
        return changed;
    }

    // Whether writing the position replaces an existing checkpoint.
    static GroupAction Write(ulong? checkpoint, MigrationOptions options) =>
        checkpoint is not null && options.Overwrite ? GroupAction.OverwriteCheckpoint : GroupAction.WriteCheckpoint;

    /// <summary>
    /// Resolves where one 1.0 subscription starts after the import, in the same order as
    /// <c>SubscriptionLoop.GetStartPositionAsync</c>: its own checkpoint; the group under its
    /// own id when the import writes or blocks it; the checkpoint or group of a predecessor
    /// (continueFrom), nearest first; otherwise the beginning or the end, which warns because
    /// the subscription replays.
    /// </summary>
    /// <param name="registration">The 1.0 subscription.</param>
    /// <param name="checkpoints">
    /// The checkpoint of every registration id and predecessor: null means no checkpoint.
    /// </param>
    /// <param name="groups">The planned groups by name. A group counts only when its action
    /// writes or blocks it; any other group counts as absent.</param>
    /// <param name="registry">The registry of the 1.0 hosts, for the event-type comparison.</param>
    public static SubscriptionPlan ResolveSubscription(
        SubscriptionRegistration registration,
        IReadOnlyDictionary<string, ulong?> checkpoints,
        IReadOnlyDictionary<string, GroupPlan> groups,
        EventTypeRegistry? registry
    ) {
        var id = registration.SubscriptionId;

        if (checkpoints.TryGetValue(id, out var own) && own is { } ownPosition) {
            // A group under the same id (imported, for example) still tells what 0.x received.
            var eventTypes = CompareEventTypes(registration.EventType, Filter(groups, id), registry);
            return new SubscriptionPlan {
                SubscriptionId = id, HandlerType = registration.HandlerType, StartsAfter = Number(ownPosition), Source = "own checkpoint",
                EventTypes = eventTypes.Text, IsWarning = eventTypes.Differs
            };
        }

        if (Source(groups, id) is { } ownGroup)
            return Subscription(registration, ownGroup, registry, $"0.x group '{id}'", $"0.x group '{id}' is blocked");

        foreach (var predecessor in registration.Predecessors ?? []) {
            if (checkpoints.TryGetValue(predecessor, out var inherited) && inherited is { } inheritedPosition) {
                var eventTypes = CompareEventTypes(registration.EventType, Filter(groups, predecessor), registry);
                return new SubscriptionPlan {
                    SubscriptionId = id, HandlerType = registration.HandlerType, StartsAfter = Number(inheritedPosition),
                    Source = $"checkpoint of '{predecessor}' (continueFrom)", EventTypes = eventTypes.Text, IsWarning = eventTypes.Differs
                };
            }

            if (Source(groups, predecessor) is { } predecessorGroup)
                return Subscription(registration, predecessorGroup, registry,
                    $"0.x group '{predecessor}' (continueFrom)", $"0.x group '{predecessor}' is blocked");
        }

        return new SubscriptionPlan {
            SubscriptionId = id, HandlerType = registration.HandlerType,
            StartsAfter = registration.StartFromEnd ? "end" : "beginning",
            Source = "no checkpoint, 0.x group or predecessor",
            EventTypes = CompareEventTypes(registration.EventType, null, registry).Text, IsWarning = true
        };
    }

    /// <summary>
    /// Compares the stored event type names a 1.0 subscription receives with the ones its 0.x
    /// group received: "same (n)" when they match, otherwise the added and removed names.
    /// A subscription on <see langword="object"/> receives "all"; without a registry, a filter
    /// or a parsable filter, the comparison is "-".
    /// </summary>
    /// <param name="eventType">The event type the 1.0 subscription is registered on.</param>
    /// <param name="filter">The 0.x group's event-type regular expression, or null.</param>
    /// <param name="registry">The registry of the 1.0 hosts, or null.</param>
    public static (string Text, bool Differs) CompareEventTypes(Type eventType, string? filter, EventTypeRegistry? registry) {
        if (eventType == typeof(object))
            return ("all", false);

        if (registry is null || filter is null || ZeroXGroupConfig.ParseFilterNames(filter) is not { } filterNames)
            return ("-", false);

        var names = registry.GetTypeNames(eventType);
        var added = names.Where(name => !filterNames.Contains(name, StringComparer.Ordinal)).ToList();
        var removed = filterNames.Where(name => !names.Contains(name, StringComparer.Ordinal)).OrderBy(name => name, StringComparer.Ordinal).ToList();
        if (added.Count == 0 && removed.Count == 0)
            return ($"same ({names.Count})", false);

        return (string.Join(", ", added.Select(name => "+" + name).Concat(removed.Select(name => "-" + name))), true);
    }

    // The group that a subscription continues from: one that the import writes or blocks.
    static GroupPlan? Source(IReadOnlyDictionary<string, GroupPlan> groups, string id) =>
        groups.TryGetValue(id, out var group)
            && group.Action is GroupAction.WriteCheckpoint or GroupAction.OverwriteCheckpoint or GroupAction.Blocked
            ? group
            : null;

    // The event-type filter of the group under the id, whatever its action, or null.
    static string? Filter(IReadOnlyDictionary<string, GroupPlan> groups, string id) =>
        groups.TryGetValue(id, out var group) ? group.EventTypeFilter : null;

    // The subscription row for a group that is written (its position) or blocked ("-" and a warning).
    static SubscriptionPlan Subscription(SubscriptionRegistration registration, GroupPlan group, EventTypeRegistry? registry, string source, string blockedSource) {
        var eventTypes = CompareEventTypes(registration.EventType, group.EventTypeFilter, registry);
        return group.IsBlocked
            ? new SubscriptionPlan {
                SubscriptionId = registration.SubscriptionId, HandlerType = registration.HandlerType, StartsAfter = "-", Source = blockedSource,
                EventTypes = eventTypes.Text, IsWarning = true
            }
            : new SubscriptionPlan {
                SubscriptionId = registration.SubscriptionId, HandlerType = registration.HandlerType, StartsAfter = Number(group.Position),
                Source = source, EventTypes = eventTypes.Text, IsWarning = eventTypes.Differs
            };
    }

    static string Number(ulong? value) => value?.ToString(CultureInfo.InvariantCulture) ?? "-";
}
