using Aggregates.KurrentDB;
using Aggregates.Subscriptions;
using Aggregates.Testing;
using FluentAssertions;

namespace Aggregates.KurrentDB.Migration;

/// <summary>
/// End-to-end scenarios for switching over from Aggregates 0.x: importing the positions of
/// its persistent subscription groups as the 1.0 checkpoints, deleting the groups, and rolling
/// a group back to where 1.0 stopped. KurrentDB only: 0.x ran on KurrentDB only.
/// </summary>
[Trait("Category", "Integration")]
public class PersistentSubscriptionMigrationTests(ITestOutputHelper output) {
    static readonly TimeSpan Linger = TimeSpan.FromSeconds(3);
    static readonly TimeSpan LongLinger = TimeSpan.FromSeconds(4);
    static readonly SerializationSetup Serialization = new(new HandlerProbe(), Orders.EventTypes);

    static CancellationToken Token => TestContext.Current.CancellationToken;

    static async Task<KurrentDbStoreFixture> StoreAsync() => (KurrentDbStoreFixture)await Stores.StartAsync(Transport.KurrentDB);

    // The subscription id of the Probe contract of <paramref name="kind"/> at <paramref name="version"/>.
    static string Id(HandlerKind kind, int version = 1) =>
        $"IntegrationTests.{NamespaceOf(kind)}.Probe@v{version}";

    static string NamespaceOf(HandlerKind kind) => kind switch {
        HandlerKind.Projection => "Projections",
        HandlerKind.Policy => "Policies",
        _ => "Sagas"
    };

    static async Task<ulong> PositionOfAsync(KurrentDbStoreFixture store, string stream) =>
        (await store.ReadAllAsync()).Single(e => e.Stream == stream).Position;

    [Theory]
    [InlineData(HandlerKind.Projection)]
    [InlineData(HandlerKind.Policy)]
    [InlineData(HandlerKind.Saga)]
    public async Task Import_ThenTheSubscriptionContinuesAfterTheGroupsPosition(HandlerKind kind) {
        await using var store = await StoreAsync();
        await using var groups = ZeroXGroups.Client(store);
        var migration = new PersistentSubscriptionMigration(store.Client, groups);
        var handler = ScenarioHandlers.Probe(kind);
        var name = Id(kind);

        await ZeroXGroups.CreateAsync(groups, name, Serialization.TypeName(typeof(OrderPlaced)), Serialization.TypeName(typeof(OrderShipped)));
        await store.AppendAsync(Serialization, "order-1", new OrderPlaced("order-1", "alice"));
        await store.AppendAsync(Serialization, "order-2", new OrderPlaced("order-2", "bob"));
        await ZeroXGroups.ConsumeAsync(groups, name, count: 2, linger: Linger);

        var plan = await migration.PlanAsync(MigrationStep.Import, cancellationToken: Token);
        var group = plan.Groups.Should().ContainSingle().Subject;
        group.State.Should().Be(GroupState.Exact);
        group.Position.Should().Be(await PositionOfAsync(store, "order-2"));
        group.Action.Should().Be(GroupAction.WriteCheckpoint);
        await migration.ApplyAsync(plan, Token);

        await using var host = await TestHost.StartAsync(store, output, o => o.Events(Orders.EventTypes).Handlers(kind, handler));
        await store.AppendAsync(host.Serialization, "order-3", new OrderPlaced("order-3", "carol"));

        await Eventually.UntilAsync(() => host.Probe.Count(handler) >= 1, "the host handles order-3");
        await Eventually.QuietAsync();
        host.Probe.Events(handler).Should().Equal(new OrderPlaced("order-3", "carol"));
    }

    [Fact]
    public async Task Plan_WritesNothing() {
        await using var store = await StoreAsync();
        await using var groups = ZeroXGroups.Client(store);
        var migration = new PersistentSubscriptionMigration(store.Client, groups);
        var name = Id(HandlerKind.Projection);

        await ZeroXGroups.CreateAsync(groups, name, Serialization.TypeName(typeof(OrderPlaced)));
        await store.AppendAsync(Serialization, "order-1", new OrderPlaced("order-1", "alice"));
        await ZeroXGroups.ConsumeAsync(groups, name, count: 1, linger: Linger);

        var plan = await migration.PlanAsync(MigrationStep.Import, cancellationToken: Token);

        plan.ToString().Should().Contain(name).And.Contain("WriteCheckpoint");
        (await new KurrentDbCheckpointStore(store.Client).GetAsync(name, Token)).Should().BeNull("the plan writes nothing");
    }

    [Fact]
    public async Task NotDrained_IsBlocked_UnlessAtLeastOnceIsAllowed() {
        await using var store = await StoreAsync();
        await using var groups = ZeroXGroups.Client(store);
        var migration = new PersistentSubscriptionMigration(store.Client, groups);
        var name = Id(HandlerKind.Projection);
        var positionOf12 = 0UL;

        await ZeroXGroups.CreateAsync(groups, name, Serialization.TypeName(typeof(OrderPlaced)));
        for (var i = 1; i <= 15; i++)
            await store.AppendAsync(Serialization, $"order-{i}", new OrderPlaced($"order-{i}", "alice"));
        positionOf12 = await PositionOfAsync(store, "order-12");
        // Lingers long enough for the server to checkpoint after the twelfth event.
        await ZeroXGroups.ConsumeAsync(groups, name, count: 12, linger: LongLinger);

        var plan = await migration.PlanAsync(MigrationStep.Import, cancellationToken: Token);
        var group = plan.Groups.Should().ContainSingle().Subject;
        group.State.Should().Be(GroupState.AtLeastOnce);
        group.Position.Should().Be(positionOf12);
        group.Action.Should().Be(GroupAction.Blocked);
        plan.CanApply.Should().BeFalse();

        var apply = () => migration.ApplyAsync(plan, Token);
        await apply.Should().ThrowAsync<InvalidOperationException>();

        var allowed = await migration.PlanAsync(MigrationStep.Import, new() { AllowAtLeastOnce = true }, Token);
        allowed.Groups.Should().ContainSingle().Which.Action.Should().Be(GroupAction.WriteCheckpoint);
        await migration.ApplyAsync(allowed, Token);
        (await new KurrentDbCheckpointStore(store.Client).GetAsync(name, Token)).Should().Be(positionOf12);
    }

    [Fact]
    public async Task ConnectedGroup_IsBlocked() {
        await using var store = await StoreAsync();
        await using var groups = ZeroXGroups.Client(store);
        var migration = new PersistentSubscriptionMigration(store.Client, groups);
        var name = Id(HandlerKind.Projection);

        await ZeroXGroups.CreateAsync(groups, name, Serialization.TypeName(typeof(OrderPlaced)));
        await store.AppendAsync(Serialization, "order-1", new OrderPlaced("order-1", "alice"));

        await using (await ZeroXGroups.ConnectAsync(groups, name)) {
            var plan = await migration.PlanAsync(MigrationStep.Import, cancellationToken: Token);
            var group = plan.Groups.Should().ContainSingle().Subject;
            group.State.Should().Be(GroupState.Connected);
            group.Action.Should().Be(GroupAction.Blocked);
            plan.CanApply.Should().BeFalse();

            var apply = () => migration.ApplyAsync(plan, Token);
            await apply.Should().ThrowAsync<InvalidOperationException>();
        }
    }

    [Fact]
    public async Task ExistingCheckpoint_IsKept_UnlessOverwrite() {
        await using var store = await StoreAsync();
        await using var groups = ZeroXGroups.Client(store);
        var migration = new PersistentSubscriptionMigration(store.Client, groups);
        var name = Id(HandlerKind.Projection);

        await ZeroXGroups.CreateAsync(groups, name, Serialization.TypeName(typeof(OrderPlaced)));
        await store.AppendAsync(Serialization, "order-1", new OrderPlaced("order-1", "alice"));
        await ZeroXGroups.ConsumeAsync(groups, name, count: 1, linger: Linger);
        await new KurrentDbCheckpointStore(store.Client).StoreAsync(name, 1, Token);

        var plan = await migration.PlanAsync(MigrationStep.Import, cancellationToken: Token);
        var group = plan.Groups.Should().ContainSingle().Subject;
        group.Action.Should().Be(GroupAction.None);
        group.Reason.Should().Contain("Overwrite");

        var overwriting = await migration.PlanAsync(MigrationStep.Import, new() { Overwrite = true }, Token);
        overwriting.Groups.Should().ContainSingle().Which.Action.Should().Be(GroupAction.OverwriteCheckpoint);
        await migration.ApplyAsync(overwriting, Token);
        (await new KurrentDbCheckpointStore(store.Client).GetAsync(name, Token))
            .Should().Be(await PositionOfAsync(store, "order-1"));
    }

    [Fact]
    public async Task ImportTwice_IsANoOp() {
        await using var store = await StoreAsync();
        await using var groups = ZeroXGroups.Client(store);
        var migration = new PersistentSubscriptionMigration(store.Client, groups);
        var name = Id(HandlerKind.Projection);

        await ZeroXGroups.CreateAsync(groups, name, Serialization.TypeName(typeof(OrderPlaced)));
        await store.AppendAsync(Serialization, "order-1", new OrderPlaced("order-1", "alice"));
        await ZeroXGroups.ConsumeAsync(groups, name, count: 1, linger: Linger);

        await migration.ApplyAsync(await migration.PlanAsync(MigrationStep.Import, cancellationToken: Token), Token);

        var plan = await migration.PlanAsync(MigrationStep.Import, cancellationToken: Token);
        var group = plan.Groups.Should().ContainSingle().Subject;
        group.Action.Should().Be(GroupAction.None);
        group.Reason.Should().Be("Already imported.");
        plan.CanApply.Should().BeTrue();
    }

    [Fact]
    public async Task StalePlan_IsRejected() {
        await using var store = await StoreAsync();
        await using var groups = ZeroXGroups.Client(store);
        var migration = new PersistentSubscriptionMigration(store.Client, groups);
        var name = Id(HandlerKind.Projection);

        await ZeroXGroups.CreateAsync(groups, name, Serialization.TypeName(typeof(OrderPlaced)));
        await store.AppendAsync(Serialization, "order-1", new OrderPlaced("order-1", "alice"));
        await store.AppendAsync(Serialization, "order-2", new OrderPlaced("order-2", "bob"));
        await ZeroXGroups.ConsumeAsync(groups, name, count: 2, linger: Linger);

        var plan = await migration.PlanAsync(MigrationStep.Import, cancellationToken: Token);
        plan.Groups.Should().ContainSingle().Which.State.Should().Be(GroupState.Exact);

        // A writer that was not stopped: the group reads order-3 without a consumer, so it is no
        // longer drained.
        await store.AppendAsync(Serialization, "order-3", new OrderPlaced("order-3", "carol"));
        await Eventually.UntilAsync(async () =>
            (await migration.PlanAsync(MigrationStep.Import, cancellationToken: Token)).Groups.Single().State != GroupState.Exact,
            "the group reads order-3");

        var apply = () => migration.ApplyAsync(plan, Token);
        await apply.Should().ThrowAsync<InvalidOperationException>();
        (await new KurrentDbCheckpointStore(store.Client).GetAsync(name, Token))
            .Should().BeNull("the rejected plan wrote nothing");
    }

    [Fact]
    public async Task Registrations_ShowWhereEachSubscriptionStarts() {
        await using var store = await StoreAsync();
        await using var groups = ZeroXGroups.Client(store);
        var migration = new PersistentSubscriptionMigration(store.Client, groups);
        var name = Id(HandlerKind.Projection);

        await ZeroXGroups.CreateAsync(groups, name, Serialization.TypeName(typeof(OrderPlaced)));
        await store.AppendAsync(Serialization, "order-1", new OrderPlaced("order-1", "alice"));
        await ZeroXGroups.ConsumeAsync(groups, name, count: 1, linger: Linger);

        var options = new MigrationOptions {
            Registrations = [
                new SubscriptionRegistration(Id(HandlerKind.Projection, 2), typeof(object), typeof(IOrderEvent), false, [name]),
                new SubscriptionRegistration("IntegrationTests.Projections.Lonely@v1", typeof(object), typeof(IOrderEvent), false),
            ],
            EventTypeRegistry = EventTypeRegistry.FromTypes([typeof(OrderPlaced), typeof(OrderShipped)]),
        };
        var plan = await migration.PlanAsync(MigrationStep.Import, options, Token);

        plan.Subscriptions.Should().HaveCount(2);
        var successor = plan.Subscriptions[0];
        successor.SubscriptionId.Should().Be(Id(HandlerKind.Projection, 2));
        successor.Source.Should().Be($"0.x group '{name}' (continueFrom)");
        successor.StartsAfter.Should().Be((await PositionOfAsync(store, "order-1")).ToString());
        successor.EventTypes.Should().Be("+IntegrationTests.OrderShipped@v1");
        successor.IsWarning.Should().BeTrue();

        var lonely = plan.Subscriptions[1];
        lonely.SubscriptionId.Should().Be("IntegrationTests.Projections.Lonely@v1");
        lonely.StartsAfter.Should().Be("beginning");
        lonely.Source.Should().Be("no checkpoint, 0.x group or predecessor");
        lonely.IsWarning.Should().BeTrue();
    }

    [Fact]
    public async Task Import_ThenAHigherVersionAdoptsTheImportedCheckpoint() {
        await using var store = await StoreAsync();
        await using var groups = ZeroXGroups.Client(store);
        var migration = new PersistentSubscriptionMigration(store.Client, groups);
        var handler = ScenarioHandlers.ProbeV2(HandlerKind.Projection);
        var name = Id(HandlerKind.Projection);

        await ZeroXGroups.CreateAsync(groups, name, Serialization.TypeName(typeof(OrderPlaced)));
        await store.AppendAsync(Serialization, "order-1", new OrderPlaced("order-1", "alice"));
        await store.AppendAsync(Serialization, "order-2", new OrderPlaced("order-2", "bob"));
        await ZeroXGroups.ConsumeAsync(groups, name, count: 2, linger: Linger);
        await migration.ApplyAsync(await migration.PlanAsync(MigrationStep.Import, cancellationToken: Token), Token);

        await using var host = await TestHost.StartAsync(store, output, o => o.Events(Orders.EventTypes).Handlers(HandlerKind.Projection, handler));
        await store.AppendAsync(host.Serialization, "order-3", new OrderPlaced("order-3", "carol"));

        await Eventually.UntilAsync(() => host.Probe.Count(handler) >= 1, "the host handles order-3");
        await Eventually.QuietAsync();
        host.Probe.Events(handler).Should().Equal(new OrderPlaced("order-3", "carol"));
        (await new KurrentDbCheckpointStore(store.Client).GetAsync(Id(HandlerKind.Projection, 2), Token))
            .Should().NotBeNull("v2 adopted the imported checkpoint");
    }

    [Fact]
    public async Task OnlyRegistered_SelectsTheGroupsOfTheRegistrations() {
        await using var store = await StoreAsync();
        await using var groups = ZeroXGroups.Client(store);
        var migration = new PersistentSubscriptionMigration(store.Client, groups);
        var name = Id(HandlerKind.Projection);

        await ZeroXGroups.CreateAsync(groups, name, Serialization.TypeName(typeof(OrderPlaced)));
        await ZeroXGroups.CreateAsync(groups, "IntegrationTests.Projections.Other@v1", Serialization.TypeName(typeof(OrderPlaced)));

        var options = new MigrationOptions {
            Registrations = [new SubscriptionRegistration(name, typeof(object), typeof(IOrderEvent), false)],
            OnlyRegistered = true,
        };
        var plan = await migration.PlanAsync(MigrationStep.Import, options, Token);

        plan.Groups.Should().ContainSingle().Which.GroupName.Should().Be(name);
    }

    [Fact]
    public async Task Delete_DeletesOnlyImportedGroups() {
        await using var store = await StoreAsync();
        await using var groups = ZeroXGroups.Client(store);
        var migration = new PersistentSubscriptionMigration(store.Client, groups);
        var name = Id(HandlerKind.Projection);
        var other = "IntegrationTests.Projections.Other@v1";

        await ZeroXGroups.CreateAsync(groups, name, Serialization.TypeName(typeof(OrderPlaced)));
        await store.AppendAsync(Serialization, "order-1", new OrderPlaced("order-1", "alice"));
        await ZeroXGroups.ConsumeAsync(groups, name, count: 1, linger: Linger);
        await migration.ApplyAsync(await migration.PlanAsync(MigrationStep.Import, cancellationToken: Token), Token);
        await ZeroXGroups.CreateAsync(groups, other, Serialization.TypeName(typeof(OrderPlaced)));

        var plan = await migration.PlanAsync(MigrationStep.Delete, cancellationToken: Token);
        plan.Groups.Should().HaveCount(2);
        plan.Groups.Should().Contain(group => group.GroupName == name && group.Action == GroupAction.DeleteGroup);
        plan.Groups.Should().Contain(group => group.GroupName == other && group.Action == GroupAction.Blocked);

        var deleting = await migration.PlanAsync(MigrationStep.Delete, new() { IncludeGroup = groupName => groupName == name }, Token);
        await migration.ApplyAsync(deleting, Token);

        var remaining = await groups.ListAllAsync(cancellationToken: Token);
        remaining.Select(group => group.GroupName).Should().NotContain(name);
        remaining.Select(group => group.GroupName).Should().Contain(other);
    }

    [Fact]
    public async Task Delete_WithParkedMessages_IsBlocked_UnlessDiscarded() {
        await using var store = await StoreAsync();
        await using var groups = ZeroXGroups.Client(store);
        var migration = new PersistentSubscriptionMigration(store.Client, groups);
        var name = Id(HandlerKind.Projection);

        await ZeroXGroups.CreateAsync(groups, name, Serialization.TypeName(typeof(OrderPlaced)));
        await store.AppendAsync(Serialization, "order-1", new OrderPlaced("order-1", "alice"));
        await store.AppendAsync(Serialization, "order-2", new OrderPlaced("order-2", "bob"));
        await ZeroXGroups.ConsumeAsync(groups, name, count: 2, linger: Linger, park: e => e.OriginalEvent.EventStreamId == "order-1");
        await migration.ApplyAsync(await migration.PlanAsync(MigrationStep.Import, cancellationToken: Token), Token);

        var plan = await migration.PlanAsync(MigrationStep.Delete, cancellationToken: Token);
        var group = plan.Groups.Should().ContainSingle().Subject;
        group.ParkedMessages.Should().Be(1);
        group.Action.Should().Be(GroupAction.Blocked);
        plan.CanApply.Should().BeFalse();

        var discarding = await migration.PlanAsync(MigrationStep.Delete, new() { DiscardParkedMessages = true }, Token);
        discarding.Groups.Should().ContainSingle().Which.Action.Should().Be(GroupAction.DeleteGroup);
        await migration.ApplyAsync(discarding, Token);

        (await groups.ListAllAsync(cancellationToken: Token)).Should().BeEmpty();
    }

    [Fact]
    public async Task Rollback_RecreatesTheGroupAfterTheCheckpoint() {
        await using var store = await StoreAsync();
        await using var groups = ZeroXGroups.Client(store);
        var migration = new PersistentSubscriptionMigration(store.Client, groups);
        var name = Id(HandlerKind.Projection);

        await ZeroXGroups.CreateAsync(groups, name, Serialization.TypeName(typeof(OrderPlaced)));
        await store.AppendAsync(Serialization, "order-1", new OrderPlaced("order-1", "alice"));
        await ZeroXGroups.ConsumeAsync(groups, name, count: 1, linger: Linger);
        await migration.ApplyAsync(await migration.PlanAsync(MigrationStep.Import, cancellationToken: Token), Token);

        await store.AppendAsync(Serialization, "order-2", new OrderPlaced("order-2", "bob"));
        await store.AppendRawAsync("other-1", "IntegrationTests.Other@v1", "{}"u8.ToArray());
        await store.AppendAsync(Serialization, "order-3", new OrderPlaced("order-3", "carol"));
        await new KurrentDbCheckpointStore(store.Client).StoreAsync(name, await PositionOfAsync(store, "order-2"), Token);

        var plan = await migration.PlanAsync(MigrationStep.Rollback, cancellationToken: Token);
        var group = plan.Groups.Should().ContainSingle().Subject;
        group.Action.Should().Be(GroupAction.RecreateGroup);
        group.Checkpoint.Should().Be(await PositionOfAsync(store, "order-2"));
        await migration.ApplyAsync(plan, Token);

        var delivered = await ZeroXGroups.ConsumeAsync(groups, name, count: 1, linger: TimeSpan.Zero);
        delivered.Should().ContainSingle().Which.Should().Be(await PositionOfAsync(store, "order-3"),
            "the group starts after the 1.0 checkpoint and skips the event of another type");
    }

    [Fact]
    public async Task Rollback_UsesTheCheckpointOfTheSuccessor() {
        await using var store = await StoreAsync();
        await using var groups = ZeroXGroups.Client(store);
        var migration = new PersistentSubscriptionMigration(store.Client, groups);
        var name = Id(HandlerKind.Projection);
        var positionOf2 = 0UL;

        await ZeroXGroups.CreateAsync(groups, name, Serialization.TypeName(typeof(OrderPlaced)));
        await store.AppendAsync(Serialization, "order-1", new OrderPlaced("order-1", "alice"));
        await ZeroXGroups.ConsumeAsync(groups, name, count: 1, linger: Linger);
        await migration.ApplyAsync(await migration.PlanAsync(MigrationStep.Import, cancellationToken: Token), Token);

        await store.AppendAsync(Serialization, "order-2", new OrderPlaced("order-2", "bob"));
        await store.AppendAsync(Serialization, "order-3", new OrderPlaced("order-3", "carol"));
        positionOf2 = await PositionOfAsync(store, "order-2");
        await new KurrentDbCheckpointStore(store.Client).StoreAsync(Id(HandlerKind.Projection, 2), positionOf2, Token);

        var options = new MigrationOptions {
            Registrations = [
                new SubscriptionRegistration(Id(HandlerKind.Projection, 2), typeof(object), typeof(IOrderEvent), false, [name]),
            ],
        };
        var plan = await migration.PlanAsync(MigrationStep.Rollback, options, Token);
        var group = plan.Groups.Should().ContainSingle().Subject;
        group.Action.Should().Be(GroupAction.RecreateGroup);
        group.Checkpoint.Should().Be(positionOf2, "the successor's checkpoint, not the one under the group's name");
        await migration.ApplyAsync(plan, Token);

        var delivered = await ZeroXGroups.ConsumeAsync(groups, name, count: 1, linger: TimeSpan.Zero);
        delivered.Should().ContainSingle().Which.Should().Be(await PositionOfAsync(store, "order-3"));
    }
}
