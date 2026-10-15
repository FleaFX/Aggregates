namespace Aggregates.Subscriptions;

/// <summary>
/// An event as stored, read back by an <see cref="IParkedMessageStore"/> to replay the parked message
/// that refers to it: an event of the application, or a deferred command or deadline request.
/// </summary>
/// <param name="EventType">The stored event type.</param>
/// <param name="Data">The stored data, not yet deserialized.</param>
/// <param name="Metadata">The deserialized metadata; <see cref="EventMetadata.Empty"/> when there is none.</param>
public readonly record struct StoredEntry(string EventType, ReadOnlyMemory<byte> Data, EventMetadata Metadata);
