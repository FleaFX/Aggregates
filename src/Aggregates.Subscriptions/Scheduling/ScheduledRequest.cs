namespace Aggregates.Subscriptions;

/// <summary>
/// An open request the scheduler will execute.
/// </summary>
/// <param name="Request">The stream and revision of the request: a saga stream, or the request stream of a policy.</param>
/// <param name="Due">When the request is due.</param>
/// <param name="Command">The deserialized command, or null when it could not be deserialized.</param>
/// <param name="Error">Why the command could not be deserialized, or null.</param>
/// <param name="Metadata">The metadata of the request, for the metadata scope of the command.</param>
/// <param name="CommitPosition">The commit position of the request.</param>
/// <param name="ResumeAfter">The position of the feed entry before the request; null when it was the first since the beginning.</param>
readonly record struct ScheduledRequest(EventReference Request, DateTimeOffset Due, ICommand? Command, Exception? Error, EventMetadata Metadata, ulong CommitPosition, ulong? ResumeAfter);
