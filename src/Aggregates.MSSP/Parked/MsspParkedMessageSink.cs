using Aggregates.Subscriptions;
using MSSP;

namespace Aggregates.MSSP;

/// <summary>
/// An <see cref="IParkedMessageSink"/> that persists parked messages in MSSP.
/// Each subscription gets a dedicated stream named <c>parked-{subscriptionId}</c>;
/// every call to <see cref="ParkAsync"/> appends a <c>$aggregates-parked</c> event containing
/// the commit position, the stream and revision of the event, and the exception details as JSON
/// (see <see cref="ParkedFormat.WriteParked"/>).
/// </summary>
/// <remarks>
/// Events whose type starts with <c>$</c> are never delivered to subscriptions, so parking a
/// message does not feed back into the subscriptions.
/// </remarks>
public sealed class MsspParkedMessageSink(IMsspClient client) : IParkedMessageSink {
    /// <inheritdoc/>
    public async ValueTask ParkAsync(string subscriptionId, SubscriptionMessage message, Exception exception, CancellationToken cancellationToken = default) {
        var eventData = new EventData(ParkedFormat.ParkedEventType, ParkedFormat.WriteParked(message, exception));
        await client.AppendAsync(ParkedFormat.ParkedStream(subscriptionId), StreamRevision.Any, [eventData], cancellationToken);
    }
}
