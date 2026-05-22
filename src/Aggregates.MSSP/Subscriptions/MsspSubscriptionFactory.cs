using Aggregates.MSSP.Infrastructure;
using Aggregates.Subscriptions;
using MSSP;

namespace Aggregates.MSSP.Subscriptions;

/// <summary>
/// Creates MSSP subscriptions.
/// </summary>
public sealed class MsspSubscriptionFactory(IMsspClient client, MsspOptions options) : ISubscriptionFactory {
    /// <inheritdoc />
    public ISubscription Subscribe(ulong? fromPosition, bool startFromEnd, CancellationToken cancellationToken = default) {
        if (startFromEnd)
            throw new NotSupportedException("Subscribing from the end of a stream is currently not supported in MSSP.");

        return new MsspSubscription(
            () => ValueTask.CompletedTask,
            client.SubscribeAsync(SubscriptionFilter.All, fromPosition is {} value ? new GlobalPosition(value) : GlobalPosition.Start, cancellationToken),
            options
        );
    }
}
