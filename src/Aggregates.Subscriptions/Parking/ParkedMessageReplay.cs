namespace Aggregates.Subscriptions;

/// <summary>
/// The outcome of replaying one parked message.
/// </summary>
/// <param name="Message">The parked message.</param>
/// <param name="Error">
/// Why the replay failed, or <see langword="null"/> when it succeeded. After a failure the message
/// stays parked; after a success it is no longer listed.
/// </param>
public sealed record ParkedMessageReplay(ParkedMessage Message, Exception? Error) {
    /// <summary>
    /// Whether the message was handled.
    /// </summary>
    public bool Succeeded => Error is null;
}
