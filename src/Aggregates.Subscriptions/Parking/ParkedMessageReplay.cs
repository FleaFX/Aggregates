namespace Aggregates.Subscriptions;

/// <summary>
/// The outcome of replaying one parked message.
/// </summary>
public sealed record ParkedMessageReplay {
    /// <summary>
    /// The parked message.
    /// </summary>
    public required ParkedMessage Message { get; init; }

    /// <summary>
    /// Why the replay failed, or <see langword="null"/> when it succeeded. After a failure the message
    /// stays parked; after a success it is no longer listed.
    /// </summary>
    public Exception? Error { get; init; }

    /// <summary>
    /// Whether the message was handled.
    /// </summary>
    public bool Succeeded => Error is null;
}
