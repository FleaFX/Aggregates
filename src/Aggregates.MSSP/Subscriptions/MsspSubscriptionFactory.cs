using System.Text.RegularExpressions;
using Aggregates.Subscriptions;
using Grpc.Core;
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
            // MSSP starts at the given position (inclusive) and positions are consecutive, so the
            // first event after the exclusive fromPosition is at pos + 1.
            ({} pos, _) => new GlobalPosition(pos + 1),
            (null, true) => GlobalPosition.End,
            _ => GlobalPosition.Start
        };

        return new MsspSubscription(
            () => ValueTask.CompletedTask,
            client.SubscribeAsync(ExcludeSystemEvents, from, cancellationToken),
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
