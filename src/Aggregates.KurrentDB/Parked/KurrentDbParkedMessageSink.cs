using System.ComponentModel;
using Aggregates.Subscriptions;
using KurrentDB.Client;

namespace Aggregates.KurrentDB.Internal;

/// <summary>
/// An <see cref="IParkedMessageSink"/> that persists parked messages in KurrentDB.
/// Each subscription gets a dedicated stream named <c>parked-{subscriptionId}</c>;
/// every call to <see cref="ParkAsync"/> appends a <c>$aggregates-parked</c> event containing
/// the commit position, the stream and revision of the event, and the exception details as JSON
/// (see <see cref="ParkedFormat.WriteParked"/>).
/// </summary>
/// <remarks>
/// <para>
/// Internal infrastructure of the Aggregates packages. Public only so that those packages can use it;
/// it is not covered by semantic versioning and may change in any release.
/// </para>
/// Events whose type starts with <c>$</c> are never delivered to subscriptions, so parking a
/// message does not feed back into the subscriptions.
/// </remarks>
[EditorBrowsable(EditorBrowsableState.Never)]
public sealed class KurrentDbParkedMessageSink(KurrentDBClient client) : IParkedMessageSink {
    /// <inheritdoc/>
    public async ValueTask ParkAsync(string subscriptionId, SubscriptionMessage message, Exception exception, CancellationToken cancellationToken = default) {
        var eventData = new EventData(Uuid.NewUuid(), ParkedFormat.ParkedEventType, ParkedFormat.WriteParked(message, exception));
        await client.AppendToStreamAsync(
            ParkedFormat.ParkedStream(subscriptionId),
            StreamState.Any,
            [eventData],
            cancellationToken: cancellationToken);
    }
}
