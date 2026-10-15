namespace Aggregates.Subscriptions;

/// <summary>
/// The content of a stored deferred command request.
/// </summary>
/// <param name="Owner">The subscription id of the saga or policy that deferred the command.</param>
/// <param name="Due">When the command is due.</param>
/// <param name="CommandType">The stored type name of the command.</param>
/// <param name="CommandData">The serialized command.</param>
public readonly record struct ScheduledCommandEnvelope(string Owner, DateTimeOffset Due, string CommandType, ReadOnlyMemory<byte> CommandData);
