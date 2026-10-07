namespace Aggregates.Testing;

/// <summary>
/// Skip reasons for integration tests of known bugs. Each describes the behaviour, so the test
/// documents what is wrong; the fix removes the skip.
/// </summary>
static class KnownIssues {
    /// <summary>
    /// Saga streams contain copies of the trigger events.
    /// </summary>
    public const string SagaEventCopies = "Sagas store a copy of their trigger event, which is delivered to every subscription again";

    /// <summary>
    /// Registration is keyed by the event type.
    /// </summary>
    public const string OneHandlerPerEventType = "Only the first handler of a kind is resolved for an event type; the second never runs";
}
