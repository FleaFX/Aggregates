using Grpc.Core;
using KurrentDB.Client;

namespace Aggregates.Testing;

/// <summary>
/// Creates and consumes persistent subscription groups the way Aggregates 0.x did.
/// </summary>
static class ZeroXGroups {
    /// <summary>
    /// A persistent subscriptions client on the connection of <paramref name="store"/>, for
    /// clients other than the store's own.
    /// </summary>
    /// <param name="store">The store to connect to.</param>
    public static KurrentDBPersistentSubscriptionsClient Client(KurrentDbStoreFixture store) =>
        new(KurrentDBClientSettings.Create(store.ConnectionString));

    /// <summary>
    /// Creates a group on <c>$all</c> with an event-type regular expression over
    /// <paramref name="eventTypeNames"/>, starting from the beginning, like 0.x did.
    /// </summary>
    /// <param name="groups">The client to create the group with.</param>
    /// <param name="groupName">The name of the group, which is the 1.0 subscription id.</param>
    /// <param name="eventTypeNames">The stored event type names the group receives.</param>
    public static Task CreateAsync(KurrentDBPersistentSubscriptionsClient groups, string groupName, params string[] eventTypeNames) =>
        groups.CreateToAllAsync(
            groupName,
            EventTypeFilter.RegularExpression($"^(?:{string.Join('|', eventTypeNames.Select(name => name.Replace(".", @"\.")))})$"),
            new PersistentSubscriptionSettings(startFrom: Position.Start),
            cancellationToken: TestContext.Current.CancellationToken);

    /// <summary>
    /// Consumes the first <paramref name="count"/> events of the group and acknowledges them
    /// (parks the ones <paramref name="park"/> returns <see langword="true"/> for), then stays
    /// connected for <paramref name="linger"/> without acknowledging anything else. Returns
    /// the commit positions of the consumed events.
    /// </summary>
    /// <param name="groups">The client to consume the group with.</param>
    /// <param name="groupName">The name of the group.</param>
    /// <param name="count">The number of events to consume and acknowledge.</param>
    /// <param name="linger">
    /// How long to stay connected after the last acknowledgement, so the acks reach the server.
    /// </param>
    /// <param name="park">Parks an event instead of acknowledging it, for parked-message scenarios.</param>
    public static async Task<IReadOnlyList<ulong>> ConsumeAsync(
        KurrentDBPersistentSubscriptionsClient groups,
        string groupName,
        int count,
        TimeSpan linger,
        Func<ResolvedEvent, bool>? park = null) {
        using var stop = CancellationTokenSource.CreateLinkedTokenSource(TestContext.Current.CancellationToken);
        stop.CancelAfter(Eventually.DefaultTimeout);

        var positions = new List<ulong>();
        try {
            await using var subscription = groups.SubscribeToAll(groupName, cancellationToken: stop.Token);
            await foreach (var message in subscription.Messages.WithCancellation(stop.Token)) {
                if (message is not PersistentSubscriptionMessage.Event @event)
                    continue;

                if (positions.Count == count)
                    // Events after the consumed ones stay unacknowledged: they return to the
                    // retry buffer when the connection ends.
                    continue;

                if (park?.Invoke(@event.ResolvedEvent) == true)
                    await subscription.Nack(PersistentSubscriptionNakEventAction.Park, "test", @event.ResolvedEvent);
                else
                    await subscription.Ack(@event.ResolvedEvent);

                positions.Add(@event.ResolvedEvent.OriginalEvent.Position.CommitPosition);
                if (positions.Count == count)
                    // Stays connected for linger after the last ack, so the acks reach the
                    // server (spike v1-07 S12).
                    stop.CancelAfter(linger);
            }
        } catch (Exception exception) when (exception is OperationCanceledException or RpcException { StatusCode: StatusCode.Cancelled }
            && stop.IsCancellationRequested && !TestContext.Current.CancellationToken.IsCancellationRequested) {
            // The timeout or the end of the linger, not the test: fall through to the count assertion.
            // The client reports the cancellation as an RpcException when its gRPC stream sees it first.
        }

        if (positions.Count < count)
            Assert.Fail($"The group '{groupName}' delivered {positions.Count} of {count} events within {Eventually.DefaultTimeout.TotalSeconds:0.#} s.");

        // The server drops the connection asynchronously; until then the group shows as connected.
        await Eventually.UntilAsync(async () =>
            !(await groups.GetInfoToAllAsync(groupName, cancellationToken: TestContext.Current.CancellationToken)).Connections.Any(),
            "the consumer is disconnected");
        return positions;
    }

    /// <summary>
    /// Connects to the group and consumes its events without acknowledging any, until the
    /// returned handle is disposed: a 0.x consumer that is connected and not caught up.
    /// </summary>
    /// <param name="groups">The client to consume the group with.</param>
    /// <param name="groupName">The name of the group.</param>
    public static async Task<IAsyncDisposable> ConnectAsync(KurrentDBPersistentSubscriptionsClient groups, string groupName) {
        var stop = CancellationTokenSource.CreateLinkedTokenSource(TestContext.Current.CancellationToken);
        var consuming = Task.Run(async () => {
            try {
                await using var subscription = groups.SubscribeToAll(groupName, cancellationToken: stop.Token);
                await foreach (var _ in subscription.Messages.WithCancellation(stop.Token)) {
                }
            } catch (OperationCanceledException) {
            }
        }, CancellationToken.None);

        await Eventually.UntilAsync(async () =>
            (await groups.GetInfoToAllAsync(groupName, cancellationToken: TestContext.Current.CancellationToken)).Connections.Any(),
            "the consumer is connected");

        return new Connection(stop, consuming);
    }

    /// <summary>
    /// The connection of <see cref="ConnectAsync"/>: ends it on disposal.
    /// </summary>
    sealed class Connection(CancellationTokenSource stop, Task consuming) : IAsyncDisposable {
        /// <summary>
        /// Ends the connection and waits for it to end.
        /// </summary>
        public async ValueTask DisposeAsync() {
            await stop.CancelAsync();
            try {
                await consuming;
            } catch (OperationCanceledException) {
            }
            stop.Dispose();
        }
    }
}
