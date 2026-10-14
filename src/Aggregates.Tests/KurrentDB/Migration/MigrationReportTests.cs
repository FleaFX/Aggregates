using FluentAssertions;

namespace Aggregates.KurrentDB.Migration;

/// <summary>
/// The dry-run report of a <see cref="MigrationPlan"/>: the counts, whether it can be applied,
/// and the tables with blocked groups and warnings first.
/// </summary>
public class MigrationReportTests {
    static GroupPlan Plan(string name, GroupAction action, GroupState state = GroupState.Exact, ulong? position = null, ulong? checkpoint = null) =>
        new(name, state, position, checkpoint, 0, null, action, "The reason.");

    [Fact]
    public void TheHeader_CountsTheGroupsPerAction() {
        var plan = new MigrationPlan(MigrationStep.Import, new(), [
            Plan("A@v1", GroupAction.WriteCheckpoint, position: 100),
            Plan("B@v1", GroupAction.WriteCheckpoint, position: 200),
            Plan("C@v1", GroupAction.None),
            Plan("D@v1", GroupAction.Blocked, GroupState.Connected),
        ], []);

        plan.ToString().Should().StartWith("Import plan: 4 groups (1 None, 2 WriteCheckpoint, 1 Blocked).\n");
    }

    [Fact]
    public void TheHeader_CountsTheSubscriptionsAndTheirWarnings() {
        var plan = new MigrationPlan(MigrationStep.Import, new(), [],
            [Subscription("A@v1", false), Subscription("B@v1", true), Subscription("C@v1", true)]);

        plan.ToString().Should().StartWith("Import plan: 0 groups; 3 subscriptions, 2 warnings.\n");
    }

    [Fact]
    public void BlockedGroups_ComeFirst_RegardlessOfTheName() {
        var plan = new MigrationPlan(MigrationStep.Import, new(), [
            Plan("B@v1", GroupAction.WriteCheckpoint, position: 100),
            Plan("A@v1", GroupAction.Blocked, GroupState.Connected),
        ], []);

        var report = plan.ToString();
        report.Should().Contain("\n! A@v1", "the blocked group is flagged");
        report.IndexOf("A@v1", StringComparison.Ordinal).Should().BeLessThan(
            report.IndexOf("B@v1", StringComparison.Ordinal), "the blocked group comes first");
    }

    [Fact]
    public void NullPositions_AndBlockedRows_ShowADash() {
        var plan = new MigrationPlan(MigrationStep.Import, new(), [
            Plan("A@v1", GroupAction.Blocked, GroupState.Connected),
        ], []);

        // The null position and checkpoint show as dashes, in cells as wide as their headers.
        plan.ToString().Should().Contain("! A@v1   Connected  -         -           0       Blocked  The reason.\n");
    }

    [Fact]
    public void WithoutSubscriptions_ThereIsNoSubscriptionTable() {
        var plan = new MigrationPlan(MigrationStep.Import, new(), [Plan("A@v1", GroupAction.WriteCheckpoint, position: 100)], []);

        plan.ToString().Should().NotContain("Subscription");
    }

    [Fact]
    public void SubscriptionWarnings_ComeFirst_WithABang() {
        var plan = new MigrationPlan(MigrationStep.Import, new(), [], [
            Subscription("B@v1", false),
            Subscription("A@v1", true),
        ]);

        var report = plan.ToString();
        report.Should().Contain("\n! A@v1", "the warning is flagged");
        report.IndexOf("A@v1", StringComparison.Ordinal).Should().BeLessThan(
            report.IndexOf("B@v1", StringComparison.Ordinal), "the warning comes first");
    }

    [Fact]
    public void APlanThatCanApply_IsReadyToApply() {
        var plan = new MigrationPlan(MigrationStep.Import, new(), [Plan("A@v1", GroupAction.WriteCheckpoint, position: 100)], []);

        plan.ToString().Should().Contain("Ready to apply.");
    }

    [Fact]
    public void ABlockedPlan_SaysSo() {
        var plan = new MigrationPlan(MigrationStep.Import, new(), [Plan("A@v1", GroupAction.Blocked, GroupState.Connected)], []);

        plan.ToString().Should().Contain("Blocked: resolve the blocked groups, or leave them out with IncludeGroup.");
    }

    [Fact]
    public void ARollbackWithoutRegistrations_WarnsAboutHigherVersions() {
        var plan = new MigrationPlan(MigrationStep.Rollback, new(), [Plan("A@v1", GroupAction.RecreateGroup, checkpoint: 100)], []);

        plan.ToString().Should().Contain("Without registrations, the 1.0 checkpoint is the one under the group's name: pass the registrations when 1.0 runs a higher version.");
    }

    static SubscriptionPlan Subscription(string id, bool warning) =>
        new(id, typeof(object), "100", "own checkpoint", "-", warning);
}
