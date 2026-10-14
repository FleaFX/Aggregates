using Aggregates.Testing;
using Aggregates.Subscriptions;
using FluentAssertions;

namespace Aggregates.KurrentDB.Migration;

/// <summary>
/// The decisions of <see cref="MigrationRules"/>: which position a 0.x group yields, and what
/// each step does with it.
/// </summary>
public class MigrationRulesTests {
    static GroupSnapshot Group(
        string name = "Ns.X@v1", string source = "$all", int connections = 0,
        ulong? lastKnown = null, ulong? lastCheckpointed = null,
        long outstanding = 0, long read = 0, long live = 0, long retry = 0, long inFlight = 0, long parked = 0) =>
        new(name, source, connections, lastKnown, lastCheckpointed, outstanding, read, live, retry, inFlight, parked);

    public class Assess {
        [Fact]
        public void NotOnAll_IsLeftAlone() {
            MigrationRules.Assess(Group(source: "orders")).Should().Be((GroupState.NotOnAll, (ulong?)null));
        }

        [Fact]
        public void Connected_HasNoPosition() {
            MigrationRules.Assess(Group(connections: 1, lastKnown: 100, lastCheckpointed: 80))
                .Should().Be((GroupState.Connected, (ulong?)null));
        }

        [Theory]
        [InlineData(100UL, 80UL, 100UL)]
        [InlineData(100UL, 200UL, 100UL)]   // the server's checkpoint drifts over skipped events (spike S16)
        [InlineData(null, 80UL, 80UL)]      // after a server restart
        [InlineData(null, 0UL, 0UL)]
        public void Drained_UsesTheLastEventRead_ElseTheCheckpoint(ulong? lastKnown, ulong? lastCheckpointed, ulong? expected) {
            MigrationRules.Assess(Group(lastKnown: lastKnown, lastCheckpointed: lastCheckpointed))
                .Should().Be((GroupState.Exact, expected));
        }

        [Fact]
        public void DrainedWithoutPositions_IsEmpty() {
            MigrationRules.Assess(Group()).Should().Be((GroupState.Empty, (ulong?)null));
        }

        [Fact]
        public void LiveBuffer_DoesNotCountAgainstDrained() {
            // The live buffer keeps events after they were delivered and acknowledged (spike S15).
            MigrationRules.Assess(Group(lastKnown: 100, live: 1)).Should().Be((GroupState.Exact, (ulong?)100));
        }

        [Theory]
        [InlineData(100UL, 80UL, 3L, 0L, 0L)]
        [InlineData(150UL, 80UL, 0L, 5L, 5L)]
        public void NotDrained_UsesTheCheckpoint(ulong? lastKnown, ulong? lastCheckpointed, long retry, long read, long live) {
            MigrationRules.Assess(Group(lastKnown: lastKnown, lastCheckpointed: lastCheckpointed, retry: retry, read: read, live: live))
                .Should().Be((GroupState.AtLeastOnce, lastCheckpointed));
        }

        [Theory]
        [InlineData(1L, 0L)]
        [InlineData(0L, 1L)]
        public void NotDrainedWithOutstandingOrInFlightMessages_IsAtLeastOnce(long outstanding, long inFlight) {
            MigrationRules.Assess(Group(lastCheckpointed: 80, outstanding: outstanding, inFlight: inFlight))
                .Should().Be((GroupState.AtLeastOnce, (ulong?)80));
        }

        [Fact]
        public void NotDrainedWithoutCheckpoint_IsUnknown() {
            MigrationRules.Assess(Group(read: 3, live: 3)).Should().Be((GroupState.Unknown, (ulong?)null));
        }
    }

    public class DecideImport {
        [Fact]
        public void NotOnAll_IsNone() {
            MigrationRules.DecideImport(Group(source: "orders"), GroupState.NotOnAll, null, null, new())
                .Should().Be((GroupAction.None, "Not a group on $all; Aggregates 0.x only created groups on $all."));
        }

        [Fact]
        public void Connected_IsBlocked_EvenWhenAlreadyImported() {
            var (action, reason) = MigrationRules.DecideImport(Group(connections: 1), GroupState.Connected, null, 80, new());
            action.Should().Be(GroupAction.Blocked);
            reason.Should().Contain("stop the 0.x host");
        }

        [Fact]
        public void ConnectedAndCaughtUp_SaysSoInTheReason() {
            var (_, reason) = MigrationRules.DecideImport(Group(connections: 1), GroupState.Connected, null, null, new());
            reason.Should().Contain("caught up:");
            reason.Should().NotContain("not caught up");
        }

        [Fact]
        public void ConnectedAndNotCaughtUp_SaysSoInTheReason() {
            var (_, reason) = MigrationRules.DecideImport(
                Group(connections: 1, outstanding: 1), GroupState.Connected, null, null, new());
            reason.Should().Contain("not caught up:");
        }

        [Fact]
        public void EqualCheckpoint_IsAlreadyImported() {
            MigrationRules.DecideImport(Group(), GroupState.Exact, 100, 100, new())
                .Should().Be((GroupAction.None, "Already imported."));
        }

        [Fact]
        public void AtLeastOnceWithAnEqualCheckpoint_IsAlreadyImported() {
            MigrationRules.DecideImport(Group(), GroupState.AtLeastOnce, 80, 80, new())
                .Should().Be((GroupAction.None, "Already imported."));
        }

        [Fact]
        public void DifferentCheckpoint_IsKept_WithoutOverwrite() {
            var (action, reason) = MigrationRules.DecideImport(Group(), GroupState.Exact, 100, 80, new());
            action.Should().Be(GroupAction.None);
            reason.Should().Contain("Overwrite");
            reason.Should().Contain("checkpoint-Ns.X@v1 exists at 80");
        }

        [Fact]
        public void DifferentCheckpoint_IsReplaced_WithOverwrite() {
            MigrationRules.DecideImport(Group(), GroupState.Exact, 100, 80, new() { Overwrite = true })
                .Action.Should().Be(GroupAction.OverwriteCheckpoint);
        }

        [Fact]
        public void Exact_IsWritten() {
            MigrationRules.DecideImport(Group(), GroupState.Exact, 100, null, new())
                .Should().Be((GroupAction.WriteCheckpoint, "Drained: every event the group read was acknowledged."));
        }

        [Fact]
        public void AtLeastOnce_IsWritten_WhenAllowed() {
            var (action, reason) = MigrationRules.DecideImport(Group(), GroupState.AtLeastOnce, 80, null, new() { AllowAtLeastOnce = true });
            action.Should().Be(GroupAction.WriteCheckpoint);
            reason.Should().Contain("handled again");
        }

        [Fact]
        public void AtLeastOnce_IsBlocked_UnlessAllowed() {
            var (action, reason) = MigrationRules.DecideImport(Group(), GroupState.AtLeastOnce, 80, null, new());
            action.Should().Be(GroupAction.Blocked);
            reason.Should().Contain("AllowAtLeastOnce");
        }

        [Fact]
        public void Unknown_IsBlocked() {
            var (action, reason) = MigrationRules.DecideImport(Group(), GroupState.Unknown, null, null, new());
            action.Should().Be(GroupAction.Blocked);
            reason.Should().Contain("caught up");
        }

        [Fact]
        public void Empty_IsNone() {
            var (action, reason) = MigrationRules.DecideImport(Group(), GroupState.Empty, null, null, new());
            action.Should().Be(GroupAction.None);
            reason.Should().Contain("read no events");
        }
    }

    public class DecideDelete {
        [Fact]
        public void NotOnAll_IsNone() {
            MigrationRules.DecideDelete(Group(source: "orders"), 100, new()).Action.Should().Be(GroupAction.None);
        }

        [Fact]
        public void Connected_IsBlocked() {
            MigrationRules.DecideDelete(Group(connections: 1), 100, new()).Action.Should().Be(GroupAction.Blocked);
        }

        [Fact]
        public void WithoutACheckpoint_IsBlocked() {
            var (action, reason) = MigrationRules.DecideDelete(Group(), null, new());
            action.Should().Be(GroupAction.Blocked);
            reason.Should().Contain("import the group first");
        }

        [Fact]
        public void ParkedMessages_BlockTheDeletion() {
            var (action, reason) = MigrationRules.DecideDelete(Group(parked: 2), 100, new());
            action.Should().Be(GroupAction.Blocked);
            reason.Should().Contain("2 parked message(s)");
        }

        [Fact]
        public void Imported_IsDeleted() {
            MigrationRules.DecideDelete(Group(), 100, new()).Should().Be((GroupAction.DeleteGroup, "Imported."));
        }

        [Fact]
        public void ParkedMessages_AreDeleted_WhenDiscarded() {
            var (action, reason) = MigrationRules.DecideDelete(Group(parked: 2), 100, new() { DiscardParkedMessages = true });
            action.Should().Be(GroupAction.DeleteGroup);
            reason.Should().Contain("deletes 2 parked message(s)");
        }
    }

    public class DecideRollback {
        [Fact]
        public void NotOnAll_IsNone() {
            MigrationRules.DecideRollback(Group(source: "orders"), 100, "^(?:Ns\\.X@v1)$", new()).Action.Should().Be(GroupAction.None);
        }

        [Fact]
        public void Connected_IsBlocked() {
            MigrationRules.DecideRollback(Group(connections: 1), 100, "^(?:Ns\\.X@v1)$", new()).Action.Should().Be(GroupAction.Blocked);
        }

        [Fact]
        public void WithoutA10Checkpoint_IsNone() {
            var (action, reason) = MigrationRules.DecideRollback(Group(), null, "^(?:Ns\\.X@v1)$", new());
            action.Should().Be(GroupAction.None);
            reason.Should().Contain("left as it is");
        }

        [Fact]
        public void WithoutAFilter_IsBlocked() {
            var (action, reason) = MigrationRules.DecideRollback(Group(), 100, null, new());
            action.Should().Be(GroupAction.Blocked);
            reason.Should().Contain("filter is unknown");
        }

        [Fact]
        public void ParkedMessages_BlockTheRollback() {
            var (action, reason) = MigrationRules.DecideRollback(Group(parked: 2), 100, "^(?:Ns\\.X@v1)$", new());
            action.Should().Be(GroupAction.Blocked);
            reason.Should().Contain("2 parked message(s)");
        }

        [Fact]
        public void Otherwise_IsRecreated() {
            var (action, reason) = MigrationRules.DecideRollback(Group(), 100, "^(?:Ns\\.X@v1)$", new());
            action.Should().Be(GroupAction.RecreateGroup);
            reason.Should().Contain("after the 1.0 checkpoint 100");
        }
    }

    public class ResolveSubscription {
        static readonly Dictionary<string, ulong?> NoCheckpoints = [];
        static readonly Dictionary<string, GroupPlan> NoGroups = [];

        static SubscriptionRegistration Registration(
            string id = "IntegrationTests.Projections.Probe@v2", bool startFromEnd = false, params string[] predecessors) =>
            new(id, typeof(object), typeof(IOrderEvent), startFromEnd, predecessors);

        static GroupPlan Written(string name, ulong position, string? filter = null) =>
            new(name, GroupState.Exact, position, null, 0, filter, GroupAction.WriteCheckpoint, "Drained.");

        static GroupPlan Blocked(string name) =>
            new(name, GroupState.Connected, null, null, 0, null, GroupAction.Blocked, "Connected.");

        [Fact]
        public void OwnCheckpoint_Wins() {
            var plan = MigrationRules.ResolveSubscription(
                Registration(), Checkpoints(("IntegrationTests.Projections.Probe@v2", 100UL)), NoGroups, null);

            plan.StartsAfter.Should().Be("100");
            plan.Source.Should().Be("own checkpoint");
            plan.IsWarning.Should().BeFalse();
        }

        [Fact]
        public void OwnCheckpoint_ComparesWithTheImportedGroupsFilter() {
            var imported = new GroupPlan("Ns.X@v1", GroupState.Exact, 100, 100, 0,
                "^(?:IntegrationTests\\.OrderPlaced@v1)$", GroupAction.None, "Already imported.");
            var plan = MigrationRules.ResolveSubscription(
                Registration("Ns.X@v1"), Checkpoints(("Ns.X@v1", 100UL)), Groups(imported),
                EventTypeRegistry.FromTypes([typeof(OrderPlaced), typeof(OrderShipped)]));

            plan.Source.Should().Be("own checkpoint");
            plan.EventTypes.Should().Be("+IntegrationTests.OrderShipped@v1");
            plan.IsWarning.Should().BeTrue();
        }

        [Fact]
        public void OwnGroupWritten_IsTheSource() {
            var plan = MigrationRules.ResolveSubscription(
                Registration("Ns.X@v1"), NoCheckpoints, Groups(Written("Ns.X@v1", 100)), null);

            plan.StartsAfter.Should().Be("100");
            plan.Source.Should().Be("0.x group 'Ns.X@v1'");
            plan.IsWarning.Should().BeFalse();
        }

        [Fact]
        public void OwnGroupBlocked_Warns() {
            var plan = MigrationRules.ResolveSubscription(
                Registration("Ns.X@v1"), NoCheckpoints, Groups(Blocked("Ns.X@v1")), null);

            plan.StartsAfter.Should().Be("-");
            plan.Source.Should().Be("0.x group 'Ns.X@v1' is blocked");
            plan.IsWarning.Should().BeTrue();
        }

        [Fact]
        public void OwnGroupEmpty_FallsThroughToThePredecessors() {
            var empty = new GroupPlan("Ns.X@v2", GroupState.Empty, null, null, 0, null, GroupAction.None, "read no events");
            var plan = MigrationRules.ResolveSubscription(
                Registration("Ns.X@v2", predecessors: "Ns.X@v1"),
                NoCheckpoints, Groups(empty, Written("Ns.X@v1", 100)), null);

            plan.Source.Should().Be("0.x group 'Ns.X@v1' (continueFrom)");
        }

        [Fact]
        public void PredecessorCheckpoint_IsContinueFrom() {
            var plan = MigrationRules.ResolveSubscription(
                Registration(predecessors: "IntegrationTests.Projections.Probe@v1"),
                Checkpoints(("IntegrationTests.Projections.Probe@v1", 100UL)), NoGroups, null);

            plan.StartsAfter.Should().Be("100");
            plan.Source.Should().Be("checkpoint of 'IntegrationTests.Projections.Probe@v1' (continueFrom)");
            plan.IsWarning.Should().BeFalse();
        }

        [Fact]
        public void PredecessorGroup_IsContinueFrom() {
            var plan = MigrationRules.ResolveSubscription(
                Registration(predecessors: "IntegrationTests.Projections.Probe@v1"), NoCheckpoints,
                Groups(Written("IntegrationTests.Projections.Probe@v1", 100)), null);

            plan.StartsAfter.Should().Be("100");
            plan.Source.Should().Be("0.x group 'IntegrationTests.Projections.Probe@v1' (continueFrom)");
        }

        [Fact]
        public void SecondPredecessor_WhenTheFirstIsMissing() {
            var plan = MigrationRules.ResolveSubscription(
                Registration("Ns.X@v3", predecessors: ["Ns.X@v2", "Ns.X@v1"]), NoCheckpoints,
                Groups(Written("Ns.X@v1", 100)), null);

            plan.Source.Should().Be("0.x group 'Ns.X@v1' (continueFrom)");
        }

        [Fact]
        public void NothingFound_StartsFromTheEnd_AndWarns() {
            var plan = MigrationRules.ResolveSubscription(Registration(startFromEnd: true), NoCheckpoints, NoGroups, null);

            plan.StartsAfter.Should().Be("end");
            plan.Source.Should().Be("no checkpoint, 0.x group or predecessor");
            plan.IsWarning.Should().BeTrue();
        }

        [Fact]
        public void NothingFound_StartsFromTheBeginning_AndWarns() {
            var plan = MigrationRules.ResolveSubscription(Registration(), NoCheckpoints, NoGroups, null);

            plan.StartsAfter.Should().Be("beginning");
            plan.IsWarning.Should().BeTrue();
        }

        [Fact]
        public void TheGroupFilterDrivesTheEventTypeComparison() {
            var registry = EventTypeRegistry.FromTypes([typeof(OrderPlaced), typeof(OrderShipped)]);
            var plan = MigrationRules.ResolveSubscription(
                Registration("Ns.X@v1"), NoCheckpoints, Groups(Written("Ns.X@v1", 100, "^(?:IntegrationTests\\.OrderPlaced@v1)$")), registry);

            plan.EventTypes.Should().Be("+IntegrationTests.OrderShipped@v1");
            plan.IsWarning.Should().BeTrue();
        }

        static Dictionary<string, ulong?> Checkpoints(params (string Id, ulong? Position)[] entries) =>
            entries.ToDictionary(entry => entry.Id, entry => entry.Position);

        static Dictionary<string, GroupPlan> Groups(params GroupPlan[] plans) =>
            plans.ToDictionary(plan => plan.GroupName);
    }

    public class ChangedGroups {
        static GroupPlan Row(string name, ulong? position = 100, GroupState state = GroupState.Exact) =>
            new(name, state, position, null, 0, null, GroupAction.WriteCheckpoint, "Imported.");

        static MigrationPlan Plan(params GroupPlan[] groups) => new(MigrationStep.Import, new(), groups, []);

        [Fact]
        public void Unchanged_IsEmpty() {
            MigrationRules.ChangedGroups(Plan(Row("A@v1"), Row("B@v1")), Plan(Row("B@v1"), Row("A@v1"))).Should().BeEmpty();
        }

        [Fact]
        public void AnExactPositionThatMoved_IsNotChanged() {
            MigrationRules.ChangedGroups(Plan(Row("A@v1")), Plan(Row("A@v1", 200))).Should().BeEmpty();
        }

        [Fact]
        public void AnAtLeastOncePositionThatMoved_IsChanged() {
            MigrationRules.ChangedGroups(
                Plan(Row("A@v1", 100, GroupState.AtLeastOnce)),
                Plan(Row("A@v1", 200, GroupState.AtLeastOnce))).Should().Equal("A@v1");
        }

        [Fact]
        public void AnotherState_IsChanged() {
            MigrationRules.ChangedGroups(Plan(Row("A@v1")), Plan(Row("A@v1", 80, GroupState.AtLeastOnce))).Should().Equal("A@v1");
        }

        [Fact]
        public void AtRollback_OnlyTheCheckpointAndTheActionCount() {
            static GroupPlan Recreate(GroupState state, ulong? position, ulong checkpoint) =>
                new("A@v1", state, position, checkpoint, 0, "^(?:A)$", GroupAction.RecreateGroup, "Created again.");
            static MigrationPlan Rollback(GroupPlan group) => new(MigrationStep.Rollback, new(), [group], []);

            MigrationRules.ChangedGroups(
                Rollback(Recreate(GroupState.Exact, 100, 300)),
                Rollback(Recreate(GroupState.AtLeastOnce, 80, 300))).Should().BeEmpty();
            MigrationRules.ChangedGroups(
                Rollback(Recreate(GroupState.Exact, 100, 300)),
                Rollback(Recreate(GroupState.Exact, 100, 400))).Should().Equal("A@v1");
        }

        [Fact]
        public void AGroupReplacedByAnother_NamesBoth() {
            MigrationRules.ChangedGroups(Plan(Row("A@v1")), Plan(Row("B@v1"))).Should().BeEquivalentTo(["A@v1", "B@v1"]);
        }
    }

    public class CompareEventTypes {
        static readonly EventTypeRegistry Registry = EventTypeRegistry.FromTypes([typeof(OrderPlaced), typeof(OrderShipped)]);
        const string PlacedAndShipped = "^(?:IntegrationTests\\.OrderPlaced@v1|IntegrationTests\\.OrderShipped@v1)$";

        [Fact]
        public void TheSameNames_AreSame() {
            MigrationRules.CompareEventTypes(typeof(IOrderEvent), PlacedAndShipped, Registry)
                .Should().Be(("same (2)", false));
        }

        [Fact]
        public void ANameOnlyIn10_IsAdded() {
            MigrationRules.CompareEventTypes(typeof(IOrderEvent), "^(?:IntegrationTests\\.OrderPlaced@v1)$", Registry)
                .Should().Be(("+IntegrationTests.OrderShipped@v1", true));
        }

        [Fact]
        public void ANameOnlyIn0x_IsRemoved() {
            MigrationRules.CompareEventTypes(typeof(OrderPlaced), PlacedAndShipped, Registry)
                .Should().Be(("-IntegrationTests.OrderShipped@v1", true));
        }

        [Fact]
        public void AddedAndRemoved_NamesAreBothReported() {
            MigrationRules.CompareEventTypes(
                    typeof(IOrderEvent), "^(?:IntegrationTests\\.OrderPlaced@v1|IntegrationTests\\.CustomerRegistered@v1)$", Registry)
                .Should().Be(("+IntegrationTests.OrderShipped@v1, -IntegrationTests.CustomerRegistered@v1", true));
        }

        [Fact]
        public void AnObjectHandler_ReceivesAll() {
            MigrationRules.CompareEventTypes(typeof(object), PlacedAndShipped, Registry).Should().Be(("all", false));
        }

        [Fact]
        public void WithoutARegistry_ThereIsNoComparison() {
            MigrationRules.CompareEventTypes(typeof(IOrderEvent), PlacedAndShipped, null).Should().Be(("-", false));
        }

        [Fact]
        public void WithoutAFilter_ThereIsNoComparison() {
            MigrationRules.CompareEventTypes(typeof(IOrderEvent), null, Registry).Should().Be(("-", false));
        }

        [Fact]
        public void WithAFilterOfAnotherShape_ThereIsNoComparison() {
            MigrationRules.CompareEventTypes(typeof(IOrderEvent), "^IntegrationTests\\..*$", Registry).Should().Be(("-", false));
        }
    }
}
