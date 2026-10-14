using KurrentDB.Client;

namespace Aggregates.KurrentDB.Migration;

/// <summary>
/// The facts of one persistent subscription group that the migration decides on.
/// </summary>
readonly record struct GroupSnapshot(
    string GroupName,
    string EventSource,
    int Connections,
    ulong? LastKnownPosition,
    ulong? LastCheckpointedPosition,
    long OutstandingMessages,
    long ReadBuffer,
    long LiveBuffer,
    long RetryBuffer,
    long InFlightMessages,
    long ParkedMessages) {

    /// <summary>
    /// Whether no event waits to be delivered, to be delivered again, or to be acknowledged.
    /// </summary>
    /// <remarks>
    /// <see cref="LiveBuffer"/> doesn't count: it holds the events the group recently read live,
    /// also after they were delivered and acknowledged. Events that wait for delivery are in
    /// <see cref="ReadBuffer"/> (spike v1-07 S5, S15).
    /// </remarks>
    public bool IsDrained => OutstandingMessages == 0 && ReadBuffer == 0 && RetryBuffer == 0 && InFlightMessages == 0;

    /// <summary>
    /// Reads the snapshot from the server's info about the group.
    /// </summary>
    /// <param name="info">The info of one persistent subscription group.</param>
    public static GroupSnapshot From(PersistentSubscriptionInfo info) => new(
        info.GroupName, info.EventSource, info.Connections.Count(),
        Commit(info.Stats.LastKnownEventPosition), Commit(info.Stats.LastCheckpointedEventPosition),
        info.Stats.OutstandingMessagesCount, info.Stats.ReadBufferCount, info.Stats.LiveBufferCount,
        info.Stats.RetryBufferCount, info.Stats.TotalInFlightMessages, info.Stats.ParkedMessageCount);

    static ulong? Commit(IPosition? position) => position is Position p ? p.CommitPosition : null;
}
