namespace Aggregates.Subscriptions;

/// <summary>
/// A deserialized event delivered by a subscription, paired with its commit position in
/// the underlying stream.
/// </summary>
/// <param name="Event">
/// The deserialized domain event, or <see langword="null"/> when the event type is unknown.
/// </param>
/// <param name="CommitPosition">The commit position of the event in the underlying stream.</param>
public readonly record struct SubscriptionMessage(object? Event, ulong CommitPosition);

/// <summary>
/// An active subscription to an event stream. Yields <see cref="SubscriptionMessage"/> values
/// in chronological order. Disposing the subscription cancels the underlying stream.
/// </summary>
public interface ISubscription : IAsyncEnumerable<SubscriptionMessage>, IAsyncDisposable { }
