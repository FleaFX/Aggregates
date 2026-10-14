using System.Globalization;
using System.Text;

namespace Aggregates.KurrentDB.Migration;

/// <summary>
/// Formats a <see cref="MigrationPlan"/> as its dry-run report: blocked groups and warnings
/// first, then the rest ordinally by name, with every column as wide as its widest value.
/// </summary>
static class MigrationReport {
    /// <summary>
    /// Formats the plan: a header with the counts, whether it can be applied, and the tables of
    /// groups and subscriptions.
    /// </summary>
    /// <param name="plan">The plan to format.</param>
    public static string Format(MigrationPlan plan) {
        var builder = new StringBuilder();

        var counts = Enum.GetValues<GroupAction>()
            .Select(action => (Action: action, Count: plan.Groups.Count(group => group.Action == action)))
            .Where(count => count.Count > 0)
            .Select(count => $"{count.Count} {count.Action}")
            .ToList();
        builder.Append($"{plan.Step} plan: {plan.Groups.Count} groups");
        if (counts.Count > 0)
            builder.Append($" ({string.Join(", ", counts)})");
        if (plan.Subscriptions.Count > 0)
            builder.Append($"; {plan.Subscriptions.Count} subscriptions, {plan.Subscriptions.Count(subscription => subscription.IsWarning)} warnings");
        builder.Append(".\n");

        builder.Append(plan.CanApply
            ? "Ready to apply.\n"
            : "Blocked: resolve the blocked groups, or leave them out with IncludeGroup.\n");
        if (plan.Step == MigrationStep.Rollback && plan.Options.Registrations is null)
            builder.Append("Without registrations, the 1.0 checkpoint is the one under the group's name: pass the registrations when 1.0 runs a higher version.\n");

        builder.Append('\n');
        var groups = plan.Groups
            .OrderBy(group => group.IsBlocked ? 0 : 1)
            .ThenBy(group => group.GroupName, StringComparer.Ordinal)
            .ToList();
        AppendTable(
            builder,
            ["Group", "State", "Position", "Checkpoint", "Parked", "Action", "Reason"],
            [.. groups.Select(group => new[] {
                group.GroupName, group.State.ToString(), Number(group.Position), Number(group.Checkpoint),
                group.ParkedMessages.ToString(CultureInfo.InvariantCulture), group.Action.ToString(), group.Reason
            })],
            [.. groups.Select(group => group.IsBlocked)]);

        if (plan.Subscriptions.Count > 0) {
            builder.Append('\n');
            var subscriptions = plan.Subscriptions
                .OrderBy(subscription => subscription.IsWarning ? 0 : 1)
                .ThenBy(subscription => subscription.SubscriptionId, StringComparer.Ordinal)
                .ToList();
            AppendTable(
                builder,
                ["Subscription", "Starts after", "Source", "Event types"],
                [.. subscriptions.Select(subscription => new[] {
                    subscription.SubscriptionId, subscription.StartsAfter, subscription.Source, subscription.EventTypes
                })],
                [.. subscriptions.Select(subscription => subscription.IsWarning)]);
        }

        return builder.ToString();
    }

    // One table: every column as wide as its widest value, columns separated by two spaces,
    // rows flagged with "! ", the last column not padded.
    static void AppendTable(StringBuilder builder, string[] headers, IReadOnlyList<string[]> rows, IReadOnlyList<bool> warnings) {
        var widths = headers.Select((header, index) => rows.Count == 0
            ? header.Length
            : Math.Max(header.Length, rows.Max(row => row[index].Length))).ToArray();

        builder.Append("  ").Append(Join(headers, widths)).Append('\n');
        for (var index = 0; index < rows.Count; index++) {
            builder.Append(warnings[index] ? "! " : "  ").Append(Join(rows[index], widths)).Append('\n');
        }
    }

    static string Join(string[] cells, int[] widths) =>
        string.Join("  ", cells.Select((cell, index) => index == cells.Length - 1 ? cell : cell.PadRight(widths[index])));

    static string Number(ulong? value) => value?.ToString(CultureInfo.InvariantCulture) ?? "-";
}
