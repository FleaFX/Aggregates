namespace Aggregates.Subscriptions;

/// <summary>
/// Records the position of each processed message of a subscription and writes it to an
/// <see cref="ICheckpointStore"/> in batches, as configured by <see cref="SubscriptionCheckpointOptions"/>.
/// </summary>
/// <remarks>
/// A tracker belongs to a single subscription loop and is not thread-safe.
/// </remarks>
public sealed class CheckpointTracker {
    readonly ICheckpointStore _store;
    readonly string _subscriptionId;
    readonly int _maxBatchSize;
    readonly TimeSpan _maxInterval;
    readonly TimeProvider _timeProvider;

    ulong? _pendingPosition;
    int _pendingCount;
    long _pendingSince;

    /// <summary>
    /// Initializes a new <see cref="CheckpointTracker"/>.
    /// </summary>
    /// <param name="store">The store the checkpoints are written to.</param>
    /// <param name="subscriptionId">The identifier of the subscription.</param>
    /// <param name="options">The batching thresholds.</param>
    /// <param name="timeProvider">The time source for <see cref="SubscriptionCheckpointOptions.MaxInterval"/>.</param>
    public CheckpointTracker(ICheckpointStore store, string subscriptionId, SubscriptionCheckpointOptions options, TimeProvider timeProvider) {
        ArgumentOutOfRangeException.ThrowIfLessThan(options.MaxBatchSize, 1, nameof(options.MaxBatchSize));
        ArgumentOutOfRangeException.ThrowIfLessThan(options.MaxInterval, TimeSpan.Zero, nameof(options.MaxInterval));

        _store = store;
        _subscriptionId = subscriptionId;
        _maxBatchSize = options.MaxBatchSize;
        _maxInterval = options.MaxInterval;
        _timeProvider = timeProvider;
    }

    /// <summary>
    /// Records <paramref name="position"/> as processed, and writes it when
    /// <see cref="SubscriptionCheckpointOptions.MaxBatchSize"/> messages have been recorded or the
    /// oldest unwritten position is older than <see cref="SubscriptionCheckpointOptions.MaxInterval"/>.
    /// </summary>
    /// <param name="position">The commit position of the processed message.</param>
    /// <param name="cancellationToken">A cancellation token.</param>
    public ValueTask AdvanceAsync(ulong position, CancellationToken cancellationToken = default) {
        if (_pendingPosition is null)
            _pendingSince = _timeProvider.GetTimestamp();

        _pendingPosition = position;
        _pendingCount++;

        return _pendingCount >= _maxBatchSize || _timeProvider.GetElapsedTime(_pendingSince) >= _maxInterval
            ? FlushAsync(cancellationToken)
            : ValueTask.CompletedTask;
    }

    /// <summary>
    /// Writes the last recorded position, if it has not been written yet.
    /// </summary>
    /// <param name="cancellationToken">A cancellation token.</param>
    public async ValueTask FlushAsync(CancellationToken cancellationToken = default) {
        if (_pendingPosition is not { } position)
            return;

        await _store.StoreAsync(_subscriptionId, position, cancellationToken);

        // Cleared only after a successful write, so a failed write is retried by the next flush.
        _pendingPosition = null;
        _pendingCount = 0;
    }
}
