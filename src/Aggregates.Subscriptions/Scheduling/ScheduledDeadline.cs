namespace Aggregates.Subscriptions;

/// <summary>
/// A saga deadline that is due, as a <see cref="Scheduler"/> hands it to the delivery of its saga.
/// </summary>
/// <param name="Request">The saga stream and revision of the request; the stream is the saga's id.</param>
/// <param name="Due">When the deadline was due.</param>
/// <param name="Payload">The deserialized payload the saga requested the deadline with.</param>
/// <param name="Metadata">The metadata of the request: that of the event or deadline the saga reacted to.</param>
/// <param name="CommitPosition">The commit position of the request.</param>
public readonly record struct ScheduledDeadline(EventReference Request, DateTimeOffset Due, object Payload, EventMetadata Metadata, ulong CommitPosition);
