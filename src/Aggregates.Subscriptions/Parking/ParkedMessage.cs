namespace Aggregates.Subscriptions;

/// <summary>
/// A message a subscription or scheduler parked, and that was neither replayed nor dismissed yet.
/// </summary>
/// <param name="SubscriptionId">
/// The id it was parked under: a subscription id, or the id of a scheduler
/// (<c>scheduled-{subscription id}</c>) for a deferred command or a saga deadline.
/// </param>
/// <param name="Id">
/// Identifies it among the messages parked under the same subscription id, for
/// <see cref="IParkedMessages.ReplayAsync"/> and <see cref="IParkedMessages.DismissAsync"/>. It stays the
/// same as long as the message is parked. Compare it, but don't derive anything from it.
/// </param>
/// <param name="ParkedAt">When it was parked, by the clock of the store.</param>
/// <param name="Source">
/// The event it refers to (for a deferred command or a saga deadline: its request), or
/// <see langword="null"/> when the parked entry doesn't tell; such a message can only be dismissed.
/// </param>
/// <param name="CommitPosition">The commit position of the event or request.</param>
/// <param name="EventType">
/// The type name of the deserialized event, command or deadline payload, or <see langword="null"/>
/// when it could not be deserialized.
/// </param>
/// <param name="ExceptionType">The type name of the exception of the last attempt.</param>
/// <param name="ExceptionMessage">The message of the exception of the last attempt.</param>
/// <param name="StackTrace">The stack trace of the exception of the last attempt, if any.</param>
public sealed record ParkedMessage(
    string SubscriptionId,
    string Id,
    DateTimeOffset ParkedAt,
    EventReference? Source,
    ulong CommitPosition,
    string? EventType,
    string ExceptionType,
    string ExceptionMessage,
    string? StackTrace);
