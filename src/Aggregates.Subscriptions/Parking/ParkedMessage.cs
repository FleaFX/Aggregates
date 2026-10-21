namespace Aggregates.Subscriptions;

/// <summary>
/// A message a subscription or scheduler parked, and that was neither replayed nor dismissed yet.
/// </summary>
public sealed record ParkedMessage {
    /// <summary>
    /// The id it was parked under: a subscription id, or the id of a scheduler
    /// (<c>scheduled-{subscription id}</c>) for a deferred command or a saga deadline.
    /// </summary>
    public required string SubscriptionId { get; init; }

    /// <summary>
    /// Identifies it among the messages parked under the same subscription id, for
    /// <see cref="IParkedMessages.ReplayAsync"/> and <see cref="IParkedMessages.DismissAsync"/>. It stays the
    /// same as long as the message is parked. Compare it, but don't derive anything from it.
    /// </summary>
    public required string Id { get; init; }

    /// <summary>
    /// When it was parked, by the clock of the store.
    /// </summary>
    public required DateTimeOffset ParkedAt { get; init; }

    /// <summary>
    /// The event it refers to (for a deferred command or a saga deadline: its request), or
    /// <see langword="null"/> when the parked entry doesn't tell; such a message can only be dismissed.
    /// </summary>
    public EventReference? Source { get; init; }

    /// <summary>
    /// The commit position of the event or request.
    /// </summary>
    public required ulong CommitPosition { get; init; }

    /// <summary>
    /// The type name of the deserialized event, command or deadline payload, or <see langword="null"/>
    /// when it could not be deserialized.
    /// </summary>
    public string? EventType { get; init; }

    /// <summary>
    /// The type name of the exception of the last attempt.
    /// </summary>
    public required string ExceptionType { get; init; }

    /// <summary>
    /// The message of the exception of the last attempt.
    /// </summary>
    public required string ExceptionMessage { get; init; }

    /// <summary>
    /// The stack trace of the exception of the last attempt, if any.
    /// </summary>
    public string? StackTrace { get; init; }
}
