namespace Aggregates.Subscriptions;

/// <summary>
/// A deserialized event delivered by a subscription, paired with its metadata and commit
/// position in the underlying stream.
/// </summary>
/// <param name="Event">
/// The deserialized domain event, or <see langword="null"/> when the event type is unknown
/// (the <c>Deserialize</c> delegate returned <see langword="null"/>; the message is skipped, not
/// parked), when deserialization failed (see <see cref="DeserializationError"/>), or for a
/// progress message (see <see cref="IsProgress"/>).
/// </param>
/// <param name="CommitPosition">The commit position of the event in the underlying stream.</param>
/// <param name="Source">Identifies the stored event by its stream and its revision within that stream.</param>
/// <param name="Metadata">
/// The metadata associated with the event. <see cref="EventMetadata.Empty"/> when the storage
/// integration was not configured with a <c>DeserializeMetadata</c> delegate, when the
/// event was written without metadata, or when deserialization failed.
/// </param>
public readonly record struct SubscriptionMessage(object? Event, ulong CommitPosition, EventReference Source, EventMetadata Metadata) {
    /// <summary>
    /// The exception thrown by the <c>Deserialize</c> or <c>DeserializeMetadata</c> delegate, or
    /// <see langword="null"/> when the message was deserialized. When set, <see cref="Event"/> is
    /// <see langword="null"/> and the subscription parks the message instead of handling it.
    /// </summary>
    public Exception? DeserializationError { get; init; }

    /// <summary>
    /// True for a message without an event: the store passed every event up to
    /// <see cref="CommitPosition"/>, and the subscription's filter excluded those it did not
    /// deliver (KurrentDB: checkpoint reached). Never handed to a handler.
    /// </summary>
    public bool IsProgress { get; init; }

    /// <summary>
    /// A progress message at <paramref name="position"/>.
    /// </summary>
    /// <param name="position">The commit position the store has read up to.</param>
    public static SubscriptionMessage Progress(ulong position) =>
        new(null, position, default, EventMetadata.Empty) { IsProgress = true };
}

/// <summary>
/// An active subscription to an event stream. Yields <see cref="SubscriptionMessage"/> values
/// in chronological order. Disposing the subscription cancels the underlying stream.
/// </summary>
public interface ISubscription : IAsyncEnumerable<SubscriptionMessage>, IAsyncDisposable { }
