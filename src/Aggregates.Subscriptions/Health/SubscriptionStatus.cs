namespace Aggregates.Subscriptions;

/// <summary>
/// The state of one subscription or scheduler, as returned by <see cref="SubscriptionMonitor.GetStatuses"/>.
/// </summary>
public sealed record SubscriptionStatus {
    /// <summary>
    /// The subscription id, or the id of a scheduler (<c>scheduled-{subscription id}</c>).
    /// </summary>
    public required string Id { get; init; }

    /// <summary>
    /// The state.
    /// </summary>
    public required SubscriptionState State { get; init; }

    /// <summary>
    /// When it entered <see cref="State"/>; a repeated failure doesn't change it.
    /// </summary>
    public required DateTimeOffset Since { get; init; }

    /// <summary>
    /// The commit position at the latest change: the last processed position of a subscription (or the
    /// position it started after), or the position of the message it blocks on. Not updated for every
    /// processed message. Null before a subscription knows a position, and for a scheduler.
    /// </summary>
    public ulong? Position { get; init; }

    /// <summary>
    /// The number of consecutive failed attempts while retrying or blocked; 0 otherwise.
    /// </summary>
    public required int Attempts { get; init; }

    /// <summary>
    /// The latest failure while retrying or blocked, or the failure that stopped it; null otherwise.
    /// </summary>
    public Exception? Error { get; init; }
}
