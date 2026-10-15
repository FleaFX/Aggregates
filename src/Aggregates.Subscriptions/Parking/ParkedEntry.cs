namespace Aggregates.Subscriptions;

/// <summary>
/// An entry of a parked stream (<see cref="ParkedFormat.ParkedStream"/>) as an
/// <see cref="IParkedMessageStore"/> reads it: a parked message, or the record of a replay or a
/// dismissal.
/// </summary>
/// <param name="Revision">The revision of the entry in the parked stream.</param>
/// <param name="EventType">The stored event type.</param>
/// <param name="Data">The stored data.</param>
/// <param name="Created">When the store wrote the entry.</param>
public readonly record struct ParkedEntry(ulong Revision, string EventType, ReadOnlyMemory<byte> Data, DateTimeOffset Created);
