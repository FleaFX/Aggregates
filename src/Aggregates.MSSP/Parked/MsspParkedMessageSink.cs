using System.Text.Json;
using Aggregates.Subscriptions;
using MSSP;

namespace Aggregates.MSSP;

/// <summary>
/// An <see cref="IParkedMessageSink"/> that persists parked messages in MSSP.
/// Each subscription gets a dedicated stream named <c>parked-{subscriptionId}</c>;
/// every call to <see cref="ParkAsync"/> appends a <c>ParkedMessage</c> event containing
/// the commit position and exception details as JSON.
/// </summary>
public sealed class MsspParkedMessageSink(IMsspClient client) : IParkedMessageSink {
    const string ParkedEventType = "ParkedMessage";

    /// <inheritdoc/>
    public async ValueTask ParkAsync(string subscriptionId, SubscriptionMessage message, Exception exception, CancellationToken cancellationToken = default) {

        var payload = JsonSerializer.SerializeToUtf8Bytes(new {
            message.CommitPosition,
            ExceptionType = exception.GetType().FullName ?? exception.GetType().Name,
            exception.Message,
            exception.StackTrace
        });

        var eventData = new EventData(ParkedEventType, payload);
        await client.AppendAsync(StreamName(subscriptionId), StreamRevision.Any, [eventData], cancellationToken);
    }

    static string StreamName(string subscriptionId) => $"parked-{subscriptionId}";
}
