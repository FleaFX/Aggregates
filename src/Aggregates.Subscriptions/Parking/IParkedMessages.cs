namespace Aggregates.Subscriptions;

/// <summary>
/// Lists, replays and dismisses the messages that subscriptions and schedulers parked. Registered by
/// <c>AddProjections</c>, <c>AddPolicies</c> and <c>AddSagas</c>; it needs the parked-message store of a
/// storage integration (<c>AddKurrentDb()</c> or <c>AddMssp()</c> on the same builder).
/// </summary>
/// <remarks>
/// <para>
/// Messages are parked per subscription id (<c>parked-{subscription id}</c>); deferred commands and saga
/// deadlines per scheduler (<c>parked-scheduled-{subscription id}</c>). Listing and dismissing work for
/// any id. A replay runs in this host, through the handler of the subscription with that id, or of the
/// subscription that continues from it (a newer contract version); for a deferred command, through
/// the command dispatcher.
/// </para>
/// <para>
/// A replay runs next to the live subscription, after the events that followed the parked one: the
/// handler must not depend on the order between them. Don't replay the same subscription from two
/// places at once.
/// </para>
/// </remarks>
public interface IParkedMessages {
    /// <summary>
    /// The messages parked under <paramref name="subscriptionId"/> that were neither replayed nor
    /// dismissed, oldest first.
    /// </summary>
    /// <param name="subscriptionId">A subscription id, or the id of a scheduler (<c>scheduled-{subscription id}</c>).</param>
    /// <param name="cancellationToken">A cancellation token.</param>
    ValueTask<IReadOnlyList<ParkedMessage>> ListAsync(string subscriptionId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Handles the parked message with <paramref name="id"/> again: it reads the event (or
    /// request) back from the store and hands it to the handler, with the retries of
    /// <see cref="SubscriptionErrorHandlingOptions"/>. When that succeeds, the message is no longer
    /// listed; when it fails, it stays parked and the result tells why.
    /// </summary>
    /// <param name="subscriptionId">A subscription id, or the id of a scheduler (<c>scheduled-{subscription id}</c>).</param>
    /// <param name="id">The <see cref="ParkedMessage.Id"/> of the message.</param>
    /// <param name="cancellationToken">A cancellation token.</param>
    /// <exception cref="InvalidOperationException">The message is not listed: it was replayed or dismissed, or it doesn't exist.</exception>
    ValueTask<ParkedMessageReplay> ReplayAsync(string subscriptionId, string id, CancellationToken cancellationToken = default);

    /// <summary>
    /// Replays the listed messages of <paramref name="subscriptionId"/> one by one, oldest first, and
    /// stops after the first one that fails.
    /// </summary>
    /// <param name="subscriptionId">A subscription id, or the id of a scheduler (<c>scheduled-{subscription id}</c>).</param>
    /// <param name="cancellationToken">A cancellation token.</param>
    /// <returns>The outcome per replayed message, in order; only the last one can have failed.</returns>
    ValueTask<IReadOnlyList<ParkedMessageReplay>> ReplayAllAsync(string subscriptionId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Removes the parked message with <paramref name="id"/> from the list without handling it.
    /// </summary>
    /// <param name="subscriptionId">A subscription id, or the id of a scheduler (<c>scheduled-{subscription id}</c>).</param>
    /// <param name="id">The <see cref="ParkedMessage.Id"/> of the message.</param>
    /// <param name="cancellationToken">A cancellation token.</param>
    /// <exception cref="InvalidOperationException">The message is not listed: it was replayed or dismissed, or it doesn't exist.</exception>
    ValueTask DismissAsync(string subscriptionId, string id, CancellationToken cancellationToken = default);
}
