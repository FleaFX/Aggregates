namespace Aggregates.Subscriptions;

/// <summary>
/// Wraps a subscription handler invocation with retry-with-backoff and parked-message fallback.
/// </summary>
/// <remarks>
/// Inject as a singleton and share across all subscription services.
/// <list type="bullet">
///   <item>On success: returns normally.</item>
///   <item>On transient failure when attempts remain: waits with exponential backoff and retries.</item>
///   <item>On final failure: calls <see cref="IParkedMessageSink.ParkAsync"/> and returns normally,
///   allowing the subscription checkpoint to advance past this message.</item>
///   <item><see cref="OperationCanceledException"/> is always propagated — it is never swallowed
///   or counted as a retry attempt.</item>
/// </list>
/// </remarks>
public sealed class SubscriptionRetryPolicy(
    IParkedMessageSink parkedMessageSink,
    SubscriptionErrorHandlingOptions options) {

    /// <summary>
    /// Executes <paramref name="handler"/> with retry and park-on-failure semantics.
    /// </summary>
    /// <param name="handler">The handler to invoke, receiving a cancellation token.</param>
    /// <param name="subscriptionId">Subscription identifier, forwarded to the parked-message sink.</param>
    /// <param name="message">The message being processed, forwarded to the parked-message sink on failure.</param>
    /// <param name="cancellationToken">A cancellation token.</param>
    public async ValueTask ExecuteAsync(Func<CancellationToken, ValueTask> handler, string subscriptionId, SubscriptionMessage message, CancellationToken cancellationToken) {

        for (var attempt = 1; ; attempt++) {
            try {
                await handler(cancellationToken);
                return;
            } catch (OperationCanceledException) {
                throw;
            } catch (Exception) when (attempt < options.MaxRetries) {
                await Task.Delay(
                    Backoff.Delay(attempt, options.InitialDelay, options.MaxDelay, options.BackoffMultiplier),
                    cancellationToken);
            } catch (Exception ex) {
                await parkedMessageSink.ParkAsync(subscriptionId, message, ex, cancellationToken);
                return;
            }
        }
    }
}
