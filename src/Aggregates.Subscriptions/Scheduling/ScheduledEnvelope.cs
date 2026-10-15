namespace Aggregates.Subscriptions;

/// <summary>
/// The content of a stored request: a deferred command or a saga deadline.
/// </summary>
/// <param name="Owner">The subscription id of the saga or policy that made the request.</param>
/// <param name="Due">When the request is due.</param>
/// <param name="Type">The stored type name of the command or deadline payload.</param>
/// <param name="Data">The serialized command or deadline payload.</param>
/// <param name="Cause">The deadline whose reaction made the request, or <see langword="null"/>.</param>
public readonly record struct ScheduledEnvelope(string Owner, DateTimeOffset Due, string Type, ReadOnlyMemory<byte> Data, EventReference? Cause);
