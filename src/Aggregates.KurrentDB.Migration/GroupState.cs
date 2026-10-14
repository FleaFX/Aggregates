namespace Aggregates.KurrentDB.Migration;

/// <summary>
/// What the state of a 0.x persistent subscription group says about its position.
/// </summary>
public enum GroupState {
    /// <summary>
    /// No consumer is connected and nothing is buffered or in flight: every event the group
    /// read was acknowledged, so its position is exact.
    /// </summary>
    Exact,

    /// <summary>
    /// Not drained, but the server's own checkpoint is known: importing it reprocesses the
    /// events after it that 0.x already handled.
    /// </summary>
    AtLeastOnce,

    /// <summary>
    /// Drained, but the group has read no events: the 1.0 subscription starts as its
    /// contract says.
    /// </summary>
    Empty,

    /// <summary>
    /// No 0.x checkpoint and not drained: there is no safe position to import.
    /// </summary>
    Unknown,

    /// <summary>
    /// A consumer is still connected: stop the 0.x host first.
    /// </summary>
    Connected,

    /// <summary>
    /// Not a group on <c>$all</c>; Aggregates 0.x only created groups on <c>$all</c>.
    /// </summary>
    NotOnAll
}
