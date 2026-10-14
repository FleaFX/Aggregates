using Aggregates.Subscriptions;

namespace Aggregates.Testing;

/// <summary>
/// Helpers for writing to a store and reading from subscriptions in tests.
/// </summary>
static class StoreExtensions {
    /// <summary>
    /// Appends <paramref name="event"/> to <paramref name="stream"/> as Aggregates would store it,
    /// with <paramref name="metadata"/> as key/value pairs.
    /// </summary>
    public static ValueTask AppendAsync(this IStoreFixture store, SerializationSetup serialization, string stream, object @event, params (string Key, object? Value)[] metadata) =>
        store.AppendRawAsync(
            stream,
            serialization.TypeName(@event.GetType()),
            serialization.SerializeData(@event),
            metadata.Length == 0
                ? default
                : serialization.SerializeMetadata(new EventMetadata(metadata.ToDictionary(entry => entry.Key, entry => entry.Value))));

    /// <summary>
    /// Opens a subscription and returns its first <paramref name="count"/> messages, failing the
    /// test when they don't arrive within <see cref="Eventually.DefaultTimeout"/>.
    /// </summary>
    public static Task<IReadOnlyList<SubscriptionMessage>> TakeAsync(this ISubscriptionFactory factory, ulong? fromPosition, bool startFromEnd, int count) =>
        factory.TakeAsync(new SubscriptionRequest(fromPosition, startFromEnd), count);

    /// <summary>
    /// Opens a subscription and returns its first <paramref name="count"/> messages, failing the
    /// test when they don't arrive within <see cref="Eventually.DefaultTimeout"/>.
    /// </summary>
    public static async Task<IReadOnlyList<SubscriptionMessage>> TakeAsync(this ISubscriptionFactory factory, SubscriptionRequest request, int count) {
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(TestContext.Current.CancellationToken);
        timeout.CancelAfter(Eventually.DefaultTimeout);

        var messages = new List<SubscriptionMessage>();
        try {
            await using var subscription = factory.Subscribe(request, timeout.Token);
            await foreach (var message in subscription.WithCancellation(timeout.Token)) {
                messages.Add(message);
                if (messages.Count == count)
                    break;
            }
        } catch (OperationCanceledException) when (timeout.IsCancellationRequested && !TestContext.Current.CancellationToken.IsCancellationRequested) {
            Assert.Fail($"Received {messages.Count} of {count} messages within {Eventually.DefaultTimeout.TotalSeconds:0.#} s.");
        }
        return messages;
    }

    /// <summary>
    /// Opens a subscription and returns its messages up to and including the first one for which
    /// <paramref name="isLast"/> returns <see langword="true"/>, failing the test when no such
    /// message arrives within <see cref="Eventually.DefaultTimeout"/>.
    /// </summary>
    public static async Task<IReadOnlyList<SubscriptionMessage>> TakeUntilAsync(this ISubscriptionFactory factory, SubscriptionRequest request, Func<SubscriptionMessage, bool> isLast) {
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(TestContext.Current.CancellationToken);
        timeout.CancelAfter(Eventually.DefaultTimeout);

        var messages = new List<SubscriptionMessage>();
        try {
            await using var subscription = factory.Subscribe(request, timeout.Token);
            await foreach (var message in subscription.WithCancellation(timeout.Token)) {
                messages.Add(message);
                if (isLast(message))
                    break;
            }
        } catch (OperationCanceledException) when (timeout.IsCancellationRequested && !TestContext.Current.CancellationToken.IsCancellationRequested) {
            Assert.Fail($"Received {messages.Count} messages, none matching the condition, within {Eventually.DefaultTimeout.TotalSeconds:0.#} s.");
        }
        return messages;
    }
}
