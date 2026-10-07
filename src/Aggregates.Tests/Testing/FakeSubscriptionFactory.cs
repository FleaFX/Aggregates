using Aggregates.Subscriptions;

namespace Aggregates.Testing;

/// <summary>
/// An <see cref="ISubscriptionFactory"/> whose subscriptions yield a fixed list of messages, for
/// unit testing subscription services without a store. Like a live subscription, a subscription
/// starts after <c>fromPosition</c> and waits for cancellation after the last message.
/// </summary>
/// <param name="messages">The messages every subscription yields, in order of commit position.</param>
sealed class FakeSubscriptionFactory(IReadOnlyList<SubscriptionMessage> messages) : ISubscriptionFactory {
    readonly TaskCompletionSource _drained = new(TaskCreationOptions.RunContinuationsAsynchronously);

    /// <summary>
    /// Completes when the consumer asks for the message after the last one, which means it has
    /// finished processing every message.
    /// </summary>
    public Task Drained => _drained.Task;

    /// <summary>
    /// Builds <paramref name="count"/> messages with commit positions 1 to <paramref name="count"/>;
    /// <paramref name="event"/> chooses the event for each position.
    /// </summary>
    public static IReadOnlyList<SubscriptionMessage> Messages(int count, Func<ulong, object?> @event) =>
        [.. Enumerable.Range(1, count).Select(i => new SubscriptionMessage(@event((ulong)i), (ulong)i, EventMetadata.Empty))];

    /// <inheritdoc/>
    public ISubscription Subscribe(ulong? fromPosition, bool startFromEnd, CancellationToken cancellationToken = default) =>
        new Subscription([.. messages.Where(message => fromPosition is null || message.CommitPosition > fromPosition)], _drained);

    sealed class Subscription(IReadOnlyList<SubscriptionMessage> messages, TaskCompletionSource drained) : ISubscription {
        public async IAsyncEnumerator<SubscriptionMessage> GetAsyncEnumerator(CancellationToken cancellationToken = default) {
            foreach (var message in messages)
                yield return message;

            drained.TrySetResult();
            await Task.Delay(Timeout.Infinite, cancellationToken);
        }

        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }
}
