namespace Aggregates.Subscriptions;

/// <summary>
/// Persists and retrieves the checkpoint for a subscription, allowing consumers to resume
/// processing from where they left off after a restart.
/// </summary>
public interface ICheckpointStore {
    /// <summary>
    /// Retrieves the last stored checkpoint for <paramref name="subscriptionId"/>.
    /// Returns <see langword="null"/> when no checkpoint has been stored yet.
    /// </summary>
    /// <param name="subscriptionId">A unique identifier for the subscription.</param>
    /// <param name="cancellationToken">A cancellation token.</param>
    ValueTask<ulong?> GetAsync(string subscriptionId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Persists <paramref name="position"/> as the last successfully processed checkpoint
    /// for <paramref name="subscriptionId"/>.
    /// </summary>
    /// <param name="subscriptionId">A unique identifier for the subscription.</param>
    /// <param name="position">The position to store.</param>
    /// <param name="cancellationToken">A cancellation token.</param>
    ValueTask StoreAsync(string subscriptionId, ulong position, CancellationToken cancellationToken = default);
}
