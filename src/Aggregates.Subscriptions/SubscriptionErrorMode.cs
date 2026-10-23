namespace Aggregates.Subscriptions;

/// <summary>
/// What a subscription does with a message that still fails after all retries (see
/// <see cref="SubscriptionErrorHandlingOptions.MaxRetries"/>).
/// </summary>
public enum SubscriptionErrorMode {
    /// <summary>
    /// Parks the message under <c>parked-{subscription id}</c> and continues with the next one. The
    /// message can be replayed or dismissed later with <see cref="IParkedMessages"/>. The default.
    /// </summary>
    Park,

    /// <summary>
    /// Never parks: the subscription retries the message, with the backoff of
    /// <see cref="SubscriptionErrorHandlingOptions"/>, until it succeeds, and handles no later message
    /// until then. Its checkpoint stays before the message, so a restart retries it too. For handlers
    /// whose order matters, such as an outbox to an external system. A message that can't be
    /// deserialized blocks the subscription as well, until a fix is deployed. It applies to events:
    /// deferred commands and saga deadlines are always parked (see
    /// <see cref="SubscriptionContractAttribute.OnError"/>).
    /// </summary>
    Block
}
