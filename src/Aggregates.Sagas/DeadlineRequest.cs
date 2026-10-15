using Aggregates.Subscriptions;

namespace Aggregates.Sagas;

/// <summary>
/// A deadline a saga requested: a change of a saga root, which the saga commit stores in the saga
/// stream as a <see cref="ScheduledFormat.DeadlineRequestEventType"/> entry.
/// </summary>
/// <param name="Owner">The subscription id of the saga.</param>
/// <param name="Due">When the deadline is due.</param>
/// <param name="Payload">The payload delivered back to the saga.</param>
public readonly record struct DeadlineRequest(string Owner, DateTimeOffset Due, object Payload) {
    /// <summary>
    /// The deadline whose reaction requested this one; <see langword="null"/> when the saga requested it
    /// in reaction to an event.
    /// </summary>
    public EventReference? Cause { get; init; }
}
