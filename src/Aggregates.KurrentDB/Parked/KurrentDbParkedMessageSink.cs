using System.Text.Json;
using Aggregates.Subscriptions;
using KurrentDB.Client;

namespace Aggregates.KurrentDB;

/// <summary>
/// An <see cref="IParkedMessageSink"/> that persists parked messages in KurrentDB.
/// Each subscription gets a dedicated stream named <c>parked-{subscriptionId}</c>;
/// every call to <see cref="ParkAsync"/> appends a <c>$aggregates-parked</c> event containing
/// the commit position and exception details as JSON.
/// </summary>
/// <remarks>
/// Events whose type starts with <c>$</c> are never delivered to subscriptions, so parking a
/// message does not feed back into the subscriptions.
/// </remarks>
public sealed class KurrentDbParkedMessageSink(KurrentDBClient client) : IParkedMessageSink {
    const string ParkedEventType = "$aggregates-parked";

    /// <inheritdoc/>
    public async ValueTask ParkAsync(string subscriptionId, SubscriptionMessage message, Exception exception, CancellationToken cancellationToken = default) {

        var payload = JsonSerializer.SerializeToUtf8Bytes(new {
            message.CommitPosition,
            ExceptionType = exception.GetType().FullName ?? exception.GetType().Name,
            exception.Message,
            exception.StackTrace
        });

        var eventData = new EventData(Uuid.NewUuid(), ParkedEventType, payload);
        await client.AppendToStreamAsync(
            StreamName(subscriptionId),
            StreamState.Any,
            [eventData],
            cancellationToken: cancellationToken);
    }

    static string StreamName(string subscriptionId) => $"parked-{subscriptionId}";
}
