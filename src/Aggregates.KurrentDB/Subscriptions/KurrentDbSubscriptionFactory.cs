using Aggregates.Subscriptions;
using Grpc.Core;
using KurrentDB.Client;

namespace Aggregates.KurrentDB;

/// <summary>
/// Creates KurrentDB <c>$all</c> subscriptions. Deserialization is handled via
/// <see cref="KurrentDbOptions.Deserialize"/>. System events are excluded via
/// <see cref="EventTypeFilter.ExcludeSystemEvents"/>.
/// </summary>
/// <remarks>
/// Events whose type starts with <c>$</c> are never delivered; the library's own bookkeeping
/// (checkpoints, parked messages) uses that prefix.
/// </remarks>
public sealed class KurrentDbSubscriptionFactory(KurrentDBClient client, KurrentDbOptions options) : ISubscriptionFactory {
    /// <inheritdoc/>
    public ISubscription Subscribe(ulong? fromPosition, bool startFromEnd, CancellationToken cancellationToken = default) {
        var from = (fromPosition, startFromEnd) switch {
            ({ } pos, _) => FromAll.After(new Position(pos, pos)),
            (null, true) => FromAll.End,
            _ => FromAll.Start,
        };

        var filterOptions = new SubscriptionFilterOptions(EventTypeFilter.ExcludeSystemEvents());
        var subscription = client.SubscribeToAll(from, filterOptions: filterOptions, cancellationToken: cancellationToken);
        return new KurrentDbSubscription(() => subscription.DisposeAsync(), subscription.Messages, options);
    }

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
