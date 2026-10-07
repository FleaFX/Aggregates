using System.Text.RegularExpressions;
using Aggregates.Subscriptions;
using MSSP;

namespace Aggregates.MSSP;

/// <summary>
/// Creates MSSP subscriptions.
/// </summary>
/// <remarks>
/// Events whose type starts with <c>$</c> are never delivered; the library's own bookkeeping
/// (checkpoints, parked messages) uses that prefix. The filter is evaluated by the store.
/// </remarks>
/// <param name="client">The <see cref="IMsspClient"/> used to create subscriptions.</param>
/// <param name="options">The <see cref="MsspOptions"/> containing deserialization configuration.</param>
public sealed class MsspSubscriptionFactory(IMsspClient client, MsspOptions options) : ISubscriptionFactory {
    static readonly SubscriptionFilter ExcludeSystemEvents =
        SubscriptionFilter.ForEventTypePattern(new Regex("^[^$]", RegexOptions.Compiled));

    /// <inheritdoc />
    public ISubscription Subscribe(ulong? fromPosition, bool startFromEnd, CancellationToken cancellationToken = default) {
        var from = (fromPosition, startFromEnd) switch {
            ({} pos, _) => new GlobalPosition(pos),
            (null, true) => GlobalPosition.End,
            _ => GlobalPosition.Start
        };

        return new MsspSubscription(
            () => ValueTask.CompletedTask,
            client.SubscribeAsync(ExcludeSystemEvents, from, cancellationToken),
            options
        );
    }
}
