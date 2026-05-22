using Aggregates.Subscriptions;
using MSSP;

namespace Aggregates.MSSP;

/// <summary>
/// Creates MSSP subscriptions.
/// </summary>
/// <param name="client">The <see cref="IMsspClient"/> used to create subscriptions.</param>
/// <param name="options">The <see cref="MsspOptions"/> containing deserialization configuration.</param>
public sealed class MsspSubscriptionFactory(IMsspClient client, MsspOptions options) : ISubscriptionFactory {
    /// <inheritdoc />
    public ISubscription Subscribe(ulong? fromPosition, bool startFromEnd, CancellationToken cancellationToken = default) {
        var from = (fromPosition, startFromEnd) switch {
            ({} pos, _) => new GlobalPosition(pos),
            (null, true) => GlobalPosition.End,
            _ => GlobalPosition.Start
        };

        return new MsspSubscription(
            () => ValueTask.CompletedTask,
            client.SubscribeAsync(SubscriptionFilter.All, from, cancellationToken),
            options
        );
    }
}
