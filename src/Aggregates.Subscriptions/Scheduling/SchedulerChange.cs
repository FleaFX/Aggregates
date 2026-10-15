namespace Aggregates.Subscriptions;

/// <summary>
/// What applying a scheduled entry to the index changed.
/// </summary>
enum SchedulerChange {
    /// <summary>
    /// The entry changed nothing: progress, another handler's request, an unknown kind, or a duplicate.
    /// </summary>
    None,

    /// <summary>
    /// A request of this handler opened a request.
    /// </summary>
    Requested,

    /// <summary>
    /// A delivery entry closed an open request.
    /// </summary>
    Delivered,

    /// <summary>
    /// A marker entry of the scheduler's own stream.
    /// </summary>
    Marker,

    /// <summary>
    /// A request entry whose data could not be read.
    /// </summary>
    Invalid
}
