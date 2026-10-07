namespace Aggregates.Subscriptions;

/// <summary>
/// Controls how often a subscription writes its checkpoint. Positions are recorded after each
/// processed message and written to the <see cref="ICheckpointStore"/> in batches, when either
/// threshold is reached, and once more when the subscription stops.
/// </summary>
public sealed class SubscriptionCheckpointOptions {
    /// <summary>
    /// Number of processed messages after which the checkpoint is written. After a crash, at most
    /// this many messages are delivered again. Default: 100.
    /// </summary>
    public int MaxBatchSize { get; set; } = 100;

    /// <summary>
    /// Maximum age of the oldest unwritten position. The threshold is checked when a message
    /// arrives, so a subscription that goes idle keeps its last position in memory until the
    /// next message or until it stops. Default: 5 seconds.
    /// </summary>
    public TimeSpan MaxInterval { get; set; } = TimeSpan.FromSeconds(5);
}
