namespace Aggregates.Subscriptions;

/// <summary>
/// The state of one subscription or scheduler, as returned by <see cref="SubscriptionMonitor.GetStatuses"/>.
/// </summary>
/// <param name="Id">The subscription id, or the id of a scheduler (<c>scheduled-{subscription id}</c>).</param>
/// <param name="State">The state.</param>
/// <param name="Since">When it entered <paramref name="State"/>; a repeated failure doesn't change it.</param>
/// <param name="Position">
/// The commit position at the latest change: the last processed position of a subscription (or the
/// position it started after), or the position of the message it blocks on. Not updated for every
/// processed message. Null before a subscription knows a position, and for a scheduler.
/// </param>
/// <param name="Attempts">The number of consecutive failed attempts while retrying or blocked; 0 otherwise.</param>
/// <param name="Error">The latest failure while retrying or blocked, or the failure that stopped it; null otherwise.</param>
public sealed record SubscriptionStatus(
    string Id,
    SubscriptionState State,
    DateTimeOffset Since,
    ulong? Position,
    int Attempts,
    Exception? Error);
