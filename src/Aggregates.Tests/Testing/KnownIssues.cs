namespace Aggregates.Testing;

/// <summary>
/// Skip reasons for integration tests of known bugs. Each describes the behaviour, so the test
/// documents what is wrong; the fix removes the skip.
/// </summary>
static class KnownIssues {
    /// <summary>
    /// Checkpoints are stored as events in the subscribed store, so they feed back into every subscription.
    /// </summary>
    public const string CheckpointFeedback = "Checkpoint events are delivered to every subscription again, so checkpoints keep growing on an idle store";

    /// <summary>
    /// Saga streams contain copies of the trigger events.
    /// </summary>
    public const string SagaEventCopies = "Sagas store a copy of their trigger event, which is delivered to every subscription again";

    /// <summary>
    /// Subscription services don't resubscribe.
    /// </summary>
    public const string HostStopsOnSubscriptionError = "An exception outside the handler (deserialization, connection loss) stops the host instead of resubscribing";

    /// <summary>
    /// Registration is keyed by the event type.
    /// </summary>
    public const string OneHandlerPerEventType = "Only the first handler of a kind is resolved for an event type; the second never runs";

    /// <summary>
    /// MSSP's subscription start position is inclusive.
    /// </summary>
    public const string MsspStartPositionInclusive = "MSSP treats the subscription start position as inclusive, so the event at the checkpoint is delivered again";
}
