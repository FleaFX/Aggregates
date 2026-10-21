using Aggregates.KurrentDB;
using Aggregates.KurrentDB.Internal;
using Aggregates.Subscriptions;
using KurrentDB.Client;

namespace Aggregates.KurrentDB.Migration;

/// <summary>
/// Imports the positions of the persistent subscription groups that Aggregates 0.x left on a
/// KurrentDB server as the 1.0 checkpoints, and cleans up or rolls back afterwards.
/// </summary>
/// <remarks>
/// <para>
/// <see cref="PlanAsync"/> is the dry-run: it reads and writes nothing, and shows what each
/// step would do. <see cref="ApplyAsync"/> executes a plan, and refuses while any group is
/// blocked or while the store changed since the plan was made. The three steps of
/// <see cref="MigrationStep"/>: Import seeds each group's position as
/// <c>checkpoint-{group name}</c>, so the 1.0 subscription under the same id continues where
/// the group left off; Delete removes the groups once 1.0 is accepted, which closes the
/// rollback window; Rollback recreates a group that still exists at the position where the
/// 1.0 subscription stopped.
/// </para>
/// <para>
/// Build the instance with the same connection string as the 1.0 hosts, in a console or
/// script, and pass the hosts' <see cref="SubscriptionRegistration"/>s and
/// <see cref="EventTypeRegistry"/> for a report of where every 1.0 subscription starts. Stop
/// the writers, let every group catch up while the 0.x hosts still run, stop the 0.x hosts,
/// then import before the first 1.0 host starts: a 1.0 host that starts without a checkpoint
/// replays and writes one, after which the import skips it.
/// </para>
/// <para>
/// Listing the groups needs <c>$ops</c> or <c>$admins</c>; reading the groups' event-type
/// filters from <c>$persistentSubscriptionConfig</c> needs <c>$admins</c>. Without that, the
/// import and the deletion work, but the rollback and the event-type comparison fall away.
/// </para>
/// </remarks>
public sealed class PersistentSubscriptionMigration(
    KurrentDBClient client,
    KurrentDBPersistentSubscriptionsClient persistentSubscriptions) {
    readonly KurrentDbCheckpointStore _checkpoints = new(client);

    /// <summary>
    /// Makes the plan for <paramref name="step"/>: what it would do with every selected group,
    /// and where every 1.0 subscription starts at Import. Reads, never writes: the plan is the
    /// dry-run.
    /// </summary>
    /// <param name="step">The step to plan.</param>
    /// <param name="options">How much the step may do; null selects every group.</param>
    /// <param name="cancellationToken">Cancels the planning.</param>
    /// <exception cref="ArgumentException">
    /// <paramref name="options"/> sets <see cref="MigrationOptions.OnlyRegistered"/> without
    /// <see cref="MigrationOptions.Registrations"/>.
    /// </exception>
    public async Task<MigrationPlan> PlanAsync(
        MigrationStep step, MigrationOptions? options = null, CancellationToken cancellationToken = default) {
        options ??= new MigrationOptions();
        if (options.OnlyRegistered && options.Registrations is null)
            throw new ArgumentException("OnlyRegistered needs Registrations.", nameof(options));

        var infos = await persistentSubscriptions.ListAllAsync(cancellationToken: cancellationToken);
        var filters = await ReadFiltersAsync(cancellationToken);
        var registered = options.OnlyRegistered ? RegisteredNames(options.Registrations!) : null;

        var groups = new List<GroupPlan>();
        foreach (var info in infos) {
            if (options.IncludeGroup is { } include && !include(info.GroupName))
                continue;
            if (registered is not null && !registered.Contains(info.GroupName))
                continue;

            var snapshot = GroupSnapshot.From(info);
            var (state, position) = MigrationRules.Assess(snapshot);
            var checkpoint = step == MigrationStep.Rollback
                ? await RollbackCheckpointAsync(snapshot.GroupName, options.Registrations, cancellationToken)
                : await _checkpoints.GetAsync(snapshot.GroupName, cancellationToken);
            var filter = snapshot.EventSource == "$all" ? filters.GetValueOrDefault(snapshot.GroupName) : null;
            var (action, reason) = step switch {
                MigrationStep.Delete => MigrationRules.DecideDelete(snapshot, checkpoint, options),
                MigrationStep.Rollback => MigrationRules.DecideRollback(snapshot, checkpoint, filter, options),
                _ => MigrationRules.DecideImport(snapshot, state, position, checkpoint, options),
            };
            groups.Add(new GroupPlan {
                GroupName = snapshot.GroupName, State = state, Position = position, Checkpoint = checkpoint,
                ParkedMessages = snapshot.ParkedMessages, EventTypeFilter = filter, Action = action, Reason = reason
            });
        }

        var subscriptions = new List<SubscriptionPlan>();
        if (step == MigrationStep.Import && options.Registrations is { } registrations)
            subscriptions.AddRange(await ResolveSubscriptionsAsync(registrations, groups, options.EventTypeRegistry, cancellationToken));

        return new MigrationPlan { Step = step, Options = options, Groups = groups, Subscriptions = subscriptions };
    }

    /// <summary>
    /// Executes <paramref name="plan"/>: writes the checkpoints, deletes the groups or
    /// recreates them, in the order of the plan. Refuses while any group is blocked, and when
    /// the store changed since the plan was made (a 0.x host that started again in between):
    /// make a new plan then. An interrupted execution is safe to repeat: what was written
    /// already shows up as done.
    /// </summary>
    /// <param name="plan">The plan to execute, unchanged on disk.</param>
    /// <param name="cancellationToken">Cancels the execution.</param>
    /// <returns>The plan made against the current store, so a repeated execution is a no-op.</returns>
    /// <exception cref="InvalidOperationException">
    /// The plan has blocked groups, or the store changed since the plan was made.
    /// </exception>
    public async Task<MigrationPlan> ApplyAsync(MigrationPlan plan, CancellationToken cancellationToken = default) {
        if (!plan.CanApply)
            throw new InvalidOperationException(
                $"The plan has blocked groups: {string.Join(", ", plan.Groups.Where(group => group.IsBlocked).Select(group => group.GroupName))}. Resolve them, or leave them out with IncludeGroup.");

        var current = await PlanAsync(plan.Step, plan.Options, cancellationToken);
        if (MigrationRules.ChangedGroups(plan, current) is { Count: > 0 } changed)
            throw new InvalidOperationException($"The store changed since the plan was made ({string.Join(", ", changed)}). Make a new plan.");

        foreach (var group in current.Groups) {
            switch (group.Action) {
                case GroupAction.WriteCheckpoint or GroupAction.OverwriteCheckpoint:
                    await _checkpoints.StoreAsync(group.GroupName, group.Position!.Value, cancellationToken);
                    break;
                case GroupAction.DeleteGroup:
                    await persistentSubscriptions.DeleteToAllAsync(group.GroupName, cancellationToken: cancellationToken);
                    break;
                case GroupAction.RecreateGroup:
                    await RecreateAsync(group, cancellationToken);
                    break;
            }
        }
        return current;
    }

    // The ids whose 1.0 counterpart a group can have: the registration ids and their predecessors.
    static HashSet<string> RegisteredNames(IReadOnlyCollection<SubscriptionRegistration> registrations) =>
        [.. registrations.Select(registration => registration.SubscriptionId)
            .Concat(registrations.SelectMany(registration => registration.Predecessors ?? []))];

    // The groups' event-type filters, best effort: unreadable yields none.
    async Task<IReadOnlyDictionary<string, string>> ReadFiltersAsync(CancellationToken cancellationToken) {
        try {
            var result = client.ReadStreamAsync(
                Direction.Backwards, ZeroXGroupConfig.StreamName, StreamPosition.End,
                maxCount: 1, cancellationToken: cancellationToken);
            if (await result.ReadState == ReadState.StreamNotFound)
                return new Dictionary<string, string>();

            await foreach (var resolved in result.WithCancellation(cancellationToken))
                return ZeroXGroupConfig.ParseFilters(resolved.Event.Data);
            return new Dictionary<string, string>();
        } catch (AccessDeniedException) {
            // Not readable: Rollback is blocked and the event-type comparison falls away.
            return new Dictionary<string, string>();
        }
    }

    // The 1.0 checkpoint a group continues from at Rollback: of the registration under its own
    // name, else of the first successor (ordinal on id), else the one under the group's name.
    async ValueTask<ulong?> RollbackCheckpointAsync(
        string groupName, IReadOnlyCollection<SubscriptionRegistration>? registrations, CancellationToken cancellationToken) {
        if (registrations is not null) {
            if (registrations.Any(registration => string.Equals(registration.SubscriptionId, groupName, StringComparison.Ordinal)))
                return await _checkpoints.GetAsync(groupName, cancellationToken);

            var successor = registrations
                .Where(registration => registration.Predecessors?.Contains(groupName, StringComparer.Ordinal) == true)
                .OrderBy(registration => registration.SubscriptionId, StringComparer.Ordinal)
                .FirstOrDefault();
            if (successor is not null)
                return await _checkpoints.GetAsync(successor.SubscriptionId, cancellationToken);
        }
        return await _checkpoints.GetAsync(groupName, cancellationToken);
    }

    // One row per registration: where the subscription starts after the import.
    async Task<List<SubscriptionPlan>> ResolveSubscriptionsAsync(
        IReadOnlyCollection<SubscriptionRegistration> registrations,
        IReadOnlyList<GroupPlan> groups,
        EventTypeRegistry? registry,
        CancellationToken cancellationToken) {
        var checkpoints = new Dictionary<string, ulong?>();
        foreach (var id in registrations.Select(registration => registration.SubscriptionId)
            .Concat(registrations.SelectMany(registration => registration.Predecessors ?? []))
            .Distinct(StringComparer.Ordinal))
            checkpoints[id] = await _checkpoints.GetAsync(id, cancellationToken);

        var byName = groups.ToDictionary(group => group.GroupName, StringComparer.Ordinal);
        return [.. registrations.Select(registration => MigrationRules.ResolveSubscription(registration, checkpoints, byName, registry))];
    }

    // Recreates the group with its own settings and filter, starting after the 1.0 checkpoint:
    // the first event that passes the filter, or the end when there is none. startFrom is
    // inclusive, so the event at the checkpoint itself is skipped.
    async Task RecreateAsync(GroupPlan group, CancellationToken cancellationToken) {
        var settings = (await persistentSubscriptions.GetInfoToAllAsync(group.GroupName, cancellationToken: cancellationToken)).Settings
            ?? new PersistentSubscriptionSettings();
        var filter = EventTypeFilter.RegularExpression(group.EventTypeFilter!);
        var checkpoint = group.Checkpoint!.Value;

        var start = Position.End;
        await foreach (var resolved in client.ReadAllAsync(
            Direction.Forwards, new Position(checkpoint, checkpoint), filter, maxCount: 2, cancellationToken: cancellationToken)) {
            if (resolved.OriginalEvent.Position.CommitPosition > checkpoint) {
                start = resolved.OriginalEvent.Position;
                break;
            }
        }

        var recreated = new PersistentSubscriptionSettings(
            settings.ResolveLinkTos, start, settings.ExtraStatistics, settings.MessageTimeout, settings.MaxRetryCount,
            settings.LiveBufferSize, settings.ReadBatchSize, settings.HistoryBufferSize, settings.CheckPointAfter,
            settings.CheckPointLowerBound, settings.CheckPointUpperBound, settings.MaxSubscriberCount, settings.ConsumerStrategyName);

        await persistentSubscriptions.DeleteToAllAsync(group.GroupName, cancellationToken: cancellationToken);
        try {
            await persistentSubscriptions.CreateToAllAsync(group.GroupName, filter, recreated, cancellationToken: cancellationToken);
        } catch (Exception exception) when (exception is not OperationCanceledException) {
            throw new InvalidOperationException(
                $"Group '{group.GroupName}' was deleted but could not be created again. Create it on $all with the event-type filter '{group.EventTypeFilter}', starting at {start}.", exception);
        }
    }
}
