using System.ComponentModel;
using Aggregates.Subscriptions;
using Grpc.Core;
using KurrentDB.Client;

namespace Aggregates.KurrentDB.Internal;

/// <summary>
/// Creates KurrentDB <c>$all</c> subscriptions. Deserialization is handled via
/// <see cref="KurrentDbOptions.Deserialize"/>. Only the event types of the request are
/// delivered, or every event type except system events.
/// </summary>
/// <remarks>
/// <para>
/// Internal infrastructure of the Aggregates packages. Public only so that those packages can use it;
/// it is not covered by semantic versioning and may change in any release.
/// </para>
/// <para>
/// Events whose type starts with <c>$</c> are never delivered; the library's own bookkeeping
/// (checkpoints, parked messages) uses that prefix.
/// </para>
/// <para>
/// The server reports progress via <c>checkpointReached</c> once per interval of events it
/// has read (see <see cref="SubscriptionRequest.ProgressInterval"/>), which becomes a
/// progress message. The end position is read from <c>$all</c> backwards.
/// </para>
/// </remarks>
[EditorBrowsable(EditorBrowsableState.Never)]
public sealed class KurrentDbSubscriptionFactory(KurrentDBClient client, KurrentDbOptions options) : ISubscriptionFactory {
    /// <inheritdoc/>
    public ISubscription Subscribe(SubscriptionRequest request, CancellationToken cancellationToken = default) {
        var from = (request.FromPosition, request.StartFromEnd) switch {
            ({ } pos, _) => FromAll.After(new Position(pos, pos)),
            (null, true) => FromAll.End,
            _ => FromAll.Start,
        };

        IEventFilter filter = request.EventTypePattern is { } pattern
            ? EventTypeFilter.RegularExpression(pattern)
            : EventTypeFilter.ExcludeSystemEvents();
        var filterOptions = new SubscriptionFilterOptions(filter, CheckpointInterval(request.ProgressInterval));
        var subscription = client.SubscribeToAll(from, filterOptions: filterOptions, cancellationToken: cancellationToken);
        return new KurrentDbSubscription(() => subscription.DisposeAsync(), subscription.Messages, options);
    }

    /// <inheritdoc/>
    public async ValueTask<ulong?> GetEndPositionAsync(CancellationToken cancellationToken = default) {
        await foreach (var resolved in client.ReadAllAsync(Direction.Backwards, Position.End, maxCount: 1, cancellationToken: cancellationToken))
            return resolved.OriginalEvent.Position.CommitPosition;
        return null;
    }

    // The server reports progress once per checkpointInterval x 32 events it reads (spike v1-05).
    internal static uint CheckpointInterval(int progressInterval) => (uint)Math.Max(1, (progressInterval + 31) / 32);

    /// <inheritdoc/>
    /// <remarks>
    /// Not transient: missing permissions, invalid credentials, and requests the server rejects
    /// as invalid or unsupported. Everything else (connection loss, keepalive timeouts, leader
    /// changes, server restarts) is transient.
    /// </remarks>
    public bool IsTransient(Exception exception) => exception switch {
        AccessDeniedException or NotAuthenticatedException => false,
        RpcException { StatusCode: StatusCode.PermissionDenied or StatusCode.Unauthenticated
            or StatusCode.InvalidArgument or StatusCode.Unimplemented or StatusCode.FailedPrecondition } => false,
        _ => true,
    };
}
