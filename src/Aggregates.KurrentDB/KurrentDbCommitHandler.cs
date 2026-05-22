using System.Buffers.Binary;
using System.IO.Hashing;
using System.Security.Cryptography;
using System.Text;
using KurrentDB.Client;

namespace Aggregates.KurrentDB;

/// <summary>
/// Persists aggregate changes to KurrentDB. Appends uncommitted events from the ambient
/// <see cref="UnitOfWork"/> to the aggregate's stream, using optimistic concurrency control.
/// </summary>
/// <remarks>
/// Each event receives a deterministic <see cref="Uuid"/> derived from the stream name,
/// its expected position in the stream, the serialized payload hash, and the event type name.
/// This ensures idempotent writes: retrying a failed commit produces identical event IDs,
/// which KurrentDB uses to deduplicate the events silently.
/// </remarks>
public sealed class KurrentDbCommitHandler(KurrentDBClient client, KurrentDbOptions options) {
    /// <summary>
    /// Appends the single changed aggregate's events to its KurrentDB stream.
    /// </summary>
    public async ValueTask CommitAsync(UnitOfWork unitOfWork) {
        if (unitOfWork.GetChanged() is not { } aggregate)
            return;

        var changes = aggregate.AggregateRoot.GetChanges().ToList();
        if (changes.Count == 0)
            return;

        var identifier = aggregate.Identifier;
        var version = (long)aggregate.AggregateRoot.Version;

        var eventData = changes.Select((e, i) => {
            var serialized = options.Serialize!(e);
            // version is the last persisted revision; the first new event lands at version + 1,
            // the second at version + 2, etc. For a brand-new stream (version < 0) the first
            // event lands at position 0.
            var expectedPosition = version < 0 ? i : version + 1 + i;
            return new EventData(CreateEventId(identifier, expectedPosition, serialized), serialized.EventType, serialized.Data);
        });

        try {
            var expectedState = version < 0
                ? StreamState.NoStream
                : StreamState.StreamRevision((ulong)version);
            await client.AppendToStreamAsync(identifier.ToString(), expectedState, eventData);
        } catch (WrongExpectedVersionException ex) {
            throw new ConcurrencyException(
                aggregate.Identifier,
                aggregate.AggregateRoot.Version,
                new AggregateVersion(ex.ActualVersion ?? -1L));
        }
    }

    /// <summary>
    /// Creates a deterministic <see cref="Uuid"/> from the stream name, the event's expected
    /// position, the serialized payload hash, and the event type name.
    /// </summary>
    internal static Uuid CreateEventId(AggregateIdentifier identifier, long expectedPosition, SerializedEvent serialized) {
        Span<byte> buffer = stackalloc byte[512];
        var written = 0;

        written += Encoding.UTF8.GetBytes(identifier.Value, buffer[written..]);
        BinaryPrimitives.WriteInt64LittleEndian(buffer[written..], expectedPosition);
        written += sizeof(long);
        BinaryPrimitives.WriteUInt64LittleEndian(buffer[written..], XxHash64.HashToUInt64(serialized.Data.Span));
        written += sizeof(ulong);
        written += Encoding.UTF8.GetBytes(serialized.EventType, buffer[written..]);

        return Uuid.FromGuid(new Guid(MD5.HashData(buffer[..written])));
    }
}
