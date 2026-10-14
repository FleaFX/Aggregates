namespace Aggregates.Subscriptions;

/// <summary>
/// Controls how often a subscription writes its checkpoint. Positions are recorded after each
/// processed message and written to the <see cref="ICheckpointStore"/> in batches, when either
/// threshold is reached, after the store passed <see cref="MaxFilteredEvents"/> events that
/// the subscription's filter excluded, and once more when the subscription stops.
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

    /// <summary>
    /// The default of <see cref="MaxFilteredEvents"/>.
    /// </summary>
    public const int DefaultMaxFilteredEvents = 4096;

    /// <summary>
    /// Number of events the store passes without delivering them, because of the subscription's
    /// event-type filter, after which the subscription writes its position. Keep it well above
    /// the number of subscriptions on the store (in all hosts): every checkpoint is such an
    /// event for the other subscriptions. KurrentDB rounds it up to a multiple of 32; MSSP does
    /// not report progress yet. Default: 4096.
    /// </summary>
    public int MaxFilteredEvents { get; set; } = DefaultMaxFilteredEvents;
}
