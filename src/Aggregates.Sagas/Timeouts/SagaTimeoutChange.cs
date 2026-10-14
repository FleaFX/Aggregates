namespace Aggregates.Sagas;

/// <summary>
/// What applying a timeout entry to the index changed.
/// </summary>
enum SagaTimeoutChange {
    /// <summary>
    /// The entry changed nothing: progress, another saga's request, or a duplicate.
    /// </summary>
    None,

    /// <summary>
    /// A request of this saga opened a timeout.
    /// </summary>
    Requested,

    /// <summary>
    /// A delivery entry closed an open timeout.
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
