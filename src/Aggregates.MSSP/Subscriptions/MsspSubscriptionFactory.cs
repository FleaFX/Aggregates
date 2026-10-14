using System.Text.RegularExpressions;
using Aggregates.Subscriptions;
using Grpc.Core;
using MSSP;

namespace Aggregates.MSSP;

/// <summary>
/// Creates MSSP subscriptions.
/// </summary>
/// <remarks>
/// <para>
/// Events whose type starts with <c>$</c> are never delivered; the library's own bookkeeping
/// (checkpoints, parked messages) uses that prefix. The filter is evaluated by the store.
/// </para>
/// <para>
/// Only the event types of the request are delivered. MSSP does not report progress and
/// cannot tell its end position yet, so <see cref="SubscriptionRequest.ProgressInterval"/> is
/// ignored and <see cref="ISubscriptionFactory.GetEndPositionAsync"/> returns
/// <see langword="null"/>.
/// </para>
/// </remarks>
/// <param name="client">The <see cref="IMsspClient"/> used to create subscriptions.</param>
/// <param name="options">The <see cref="MsspOptions"/> containing deserialization configuration.</param>
public sealed class MsspSubscriptionFactory(IMsspClient client, MsspOptions options) : ISubscriptionFactory {
    static readonly SubscriptionFilter ExcludeSystemEvents =
        SubscriptionFilter.ForEventTypePattern(new Regex("^[^$]", RegexOptions.Compiled));

    /// <inheritdoc />
    public ISubscription Subscribe(SubscriptionRequest request, CancellationToken cancellationToken = default) {
        var from = (request.FromPosition, request.StartFromEnd) switch {
            // MSSP starts at the given position (inclusive) and positions are consecutive, so the
            // first event after the exclusive fromPosition is at pos + 1.
            ({} pos, _) => new GlobalPosition(pos + 1),
            (null, true) => GlobalPosition.End,
            _ => GlobalPosition.Start
        };

        var filter = request.EventTypePattern is { } pattern
            ? SubscriptionFilter.ForEventTypePattern(new Regex(pattern, RegexOptions.Compiled | RegexOptions.CultureInvariant))
            : ExcludeSystemEvents;

        return new MsspSubscription(
            () => ValueTask.CompletedTask,
            client.SubscribeAsync(filter, from, cancellationToken),
            options
        );
    }

    /// <inheritdoc />
    /// <remarks>
    /// Not transient: a remote server that rejects the request for missing permissions, invalid
    /// credentials, or as invalid or unsupported. Everything else (connection loss, leader
    /// changes, a stopped embedded store) is transient.
    /// </remarks>
    public bool IsTransient(Exception exception) => exception switch {
        RpcException { StatusCode: StatusCode.PermissionDenied or StatusCode.Unauthenticated
            or StatusCode.InvalidArgument or StatusCode.Unimplemented or StatusCode.FailedPrecondition } => false,
        _ => true,
    };
}
