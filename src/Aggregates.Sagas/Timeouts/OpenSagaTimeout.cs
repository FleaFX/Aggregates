using Aggregates.Subscriptions;

namespace Aggregates.Sagas;

/// <summary>
/// A timeout the scheduler will execute.
/// </summary>
/// <param name="Request">The saga stream and revision of the request; the saga id is its stream.</param>
/// <param name="Due">When the timeout is due.</param>
/// <param name="Command">The deserialized command, or null when it could not be deserialized.</param>
/// <param name="Error">Why the command could not be deserialized, or null.</param>
/// <param name="Metadata">The metadata of the request, for the metadata scope of the command.</param>
/// <param name="CommitPosition">The commit position of the request.</param>
/// <param name="ResumeAfter">The position of the feed entry before the request; null when it was the first since the beginning.</param>
readonly record struct OpenSagaTimeout(EventReference Request, DateTimeOffset Due, ICommand? Command, Exception? Error, EventMetadata Metadata, ulong CommitPosition, ulong? ResumeAfter);
