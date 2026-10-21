namespace Aggregates.Subscriptions;

/// <summary>
/// A stored checkpoint, and the predecessor it was adopted from.
/// </summary>
public readonly record struct SubscriptionCheckpoint {
    /// <summary>
    /// The commit position to continue after.
    /// </summary>
    public required ulong Position { get; init; }

    /// <summary>
    /// The index of the predecessor whose checkpoint was adopted; null when it is the subscription's own.
    /// </summary>
    public int? AdoptedFrom { get; init; }
}

/// <summary>
/// Extensions on <see cref="ICheckpointStore"/>.
/// </summary>
public static class CheckpointStoreExtensions {
    /// <summary>
    /// Gets the checkpoint of <paramref name="subscriptionId"/>; without one, adopts the checkpoint of
    /// the nearest of <paramref name="predecessorIds"/> that has one, by storing it as the
    /// subscription's own.
    /// </summary>
    /// <param name="store">The checkpoint store.</param>
    /// <param name="subscriptionId">The id whose checkpoint is read.</param>
    /// <param name="predecessorIds">The checkpoint ids of the predecessors, nearest first.</param>
    /// <param name="cancellationToken">A cancellation token.</param>
    /// <returns>The checkpoint, or <see langword="null"/> when neither the subscription nor a predecessor has one.</returns>
    public static async ValueTask<SubscriptionCheckpoint?> GetOrAdoptAsync(this ICheckpointStore store, string subscriptionId,
        IReadOnlyList<string> predecessorIds, CancellationToken cancellationToken = default) {
        if (await store.GetAsync(subscriptionId, cancellationToken) is { } own)
            return new SubscriptionCheckpoint { Position = own };
        for (var i = 0; i < predecessorIds.Count; i++) {
            if (await store.GetAsync(predecessorIds[i], cancellationToken) is not { } inherited)
                continue;
            await store.StoreAsync(subscriptionId, inherited, cancellationToken);
            return new SubscriptionCheckpoint { Position = inherited, AdoptedFrom = i };
        }
        return null;
    }
}
