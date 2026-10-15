namespace Aggregates.Subscriptions;

/// <summary>
/// A command a saga or a policy deferred: a change of a saga root, which the saga commit stores in
/// the saga stream, or a request a policy stores with <see cref="IScheduledStore.TryAppendRequestsAsync"/>.
/// Either way it is stored as a <see cref="ScheduledFormat.CommandRequestEventType"/> entry.
/// </summary>
/// <param name="Owner">The subscription id of the saga or policy that deferred the command.</param>
/// <param name="Due">When the command is due.</param>
/// <param name="Command">The command to execute.</param>
public readonly record struct DeferredCommandRequest(string Owner, DateTimeOffset Due, ICommand Command);
