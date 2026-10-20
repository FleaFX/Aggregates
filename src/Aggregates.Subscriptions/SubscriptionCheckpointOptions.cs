namespace Aggregates.Subscriptions;

/// <summary>
/// Controls how often a subscription writes its checkpoint, and whether it may start without one.
/// Positions are recorded after each processed message and written to the
/// <see cref="ICheckpointStore"/> in batches, when either threshold is reached, after the store
/// passed <see cref="MaxFilteredEvents"/> events that the subscription's filter excluded, and once
/// more when the subscription stops.
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

    /// <summary>
    /// Whether a subscription refuses to start without a checkpoint. When it has no checkpoint of
    /// its own and none of its predecessors has one to adopt, it fails with an
    /// <see cref="InvalidOperationException"/> before it subscribes, unless its id is in
    /// <see cref="AllowedWithoutCheckpoint"/>. This holds for a start from the beginning and from
    /// the end alike. It protects against a start that replays the store, or skips to its end, by
    /// mistake: 0.x positions that were not imported yet, a checkpoint store that was cleaned up,
    /// or a wrong continueFrom. It doesn't apply to the scheduler of deferred commands and saga
    /// deadlines, which reads its requests again without executing any twice. Default:
    /// <see langword="false"/>.
    /// </summary>
    public bool RequireCheckpoint { get; set; }

    /// <summary>
    /// The subscription ids that may start without a checkpoint while
    /// <see cref="RequireCheckpoint"/> is on: from the beginning, or from the end with
    /// startFromEnd. List a new subscription, or one that replays on purpose, in the deployment
    /// that first starts it; remove it once its checkpoint (<c>checkpoint-{id}</c>) exists.
    /// Compared ordinally. Default: empty.
    /// </summary>
    public IList<string> AllowedWithoutCheckpoint { get; set; } = [];
}
