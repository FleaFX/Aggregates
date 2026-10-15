namespace Aggregates.Subscriptions;

/// <summary>
/// What a scheduled request is.
/// </summary>
enum ScheduledKind {
    /// <summary>
    /// A deferred command (<see cref="ScheduledFormat.CommandRequestEventType"/>), executed through the command pipeline.
    /// </summary>
    Command,

    /// <summary>
    /// A saga deadline (<see cref="ScheduledFormat.DeadlineRequestEventType"/>), delivered back to its saga.
    /// </summary>
    Deadline
}
