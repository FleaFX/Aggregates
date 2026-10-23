using System.Buffers;
using System.Buffers.Binary;
using System.Collections.Concurrent;
using System.ComponentModel;
using System.IO.Hashing;
using System.Security.Cryptography;
using System.Text;
using KurrentDB.Client;
using Microsoft.Extensions.Logging;

namespace Aggregates.KurrentDB.Internal;

/// <summary>
/// Persists aggregate changes to KurrentDB. Appends uncommitted events from the ambient
/// <see cref="UnitOfWork"/> to the aggregate's stream, using optimistic concurrency control.
/// </summary>
/// <remarks>
/// <para>
/// Internal infrastructure of the Aggregates packages. Public only so that those packages can use it;
/// it is not covered by semantic versioning and may change in any release.
/// </para>
/// Each event receives a deterministic <see cref="Uuid"/> derived from the stream name,
/// its expected position in the stream, the serialized payload hash, and the event type name.
/// This ensures idempotent writes: retrying a failed commit produces identical event IDs,
/// which KurrentDB uses to deduplicate the events silently.
/// </remarks>
[EditorBrowsable(EditorBrowsableState.Never)]
public sealed partial class KurrentDbCommitHandler(
    KurrentDBClient client, KurrentDbOptions options, ILogger<KurrentDbCommitHandler> logger,
    EventTypeRegistry? eventTypeRegistry = null) {

    readonly ConcurrentDictionary<Type, byte> _checkedTypes = new();

    /// <summary>
    /// Appends the single changed aggregate's events to its KurrentDB stream, serialized with
    /// <see cref="KurrentDbOptions.Serialize"/>.
    /// </summary>
    /// <param name="unitOfWork">The unit of work holding the aggregate to commit.</param>
    public ValueTask CommitAsync(UnitOfWork unitOfWork) =>
        CommitAsync(unitOfWork, eventTypeRegistry is null ? options.Serialize! : SerializeAndCheck);

    // Wraps Serialize to warn, once per CLR type per process, when the stored name differs from the
    // registry's, or the registry maps no type back to the stored name: subscriptions filter on the
    // registry's names and would miss the events, and a name the registry can't read back leaves them
    // unreadable for aggregates and sagas.
    SerializedEvent SerializeAndCheck(object @event) {
        var serialized = options.Serialize!(@event);
        var type = @event.GetType();
        if (eventTypeRegistry is not null && _checkedTypes.TryAdd(type, 0)) {
            var registryName = eventTypeRegistry.GetTypeName(type);
            if (!string.Equals(registryName, serialized.EventType, StringComparison.Ordinal))
                LogTypeNameMismatch(logger, type.FullName, serialized.EventType, registryName);
            if (!eventTypeRegistry.TryGetType(serialized.EventType, out var stored) || stored != type)
                LogNotReadable(logger, type.FullName, serialized.EventType);
        }
        return serialized;
    }

    [LoggerMessage(Level = LogLevel.Warning, Message = "Event type {Type} is stored as '{StoredName}', but the EventTypeRegistry names it '{RegistryName}'. Subscriptions filter on the registry's names and will not receive these events. Serialize with the registry's names, or remove the registry from the service collection to disable filtering.")]
    static partial void LogTypeNameMismatch(ILogger logger, string? type, string storedName, string registryName);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Event type {Type} is stored as '{StoredName}', which the EventTypeRegistry doesn't map back to this type: the events are written, but can't be read back. Add the type's assembly to the registry, or serialize with the registry's names.")]
    static partial void LogNotReadable(ILogger logger, string? type, string storedName);

    /// <summary>
    /// Appends the single changed aggregate's events to its KurrentDB stream, serialized with
    /// <paramref name="serialize"/> instead of <see cref="KurrentDbOptions.Serialize"/>. Used by
    /// saga commits, whose changes are references to source events rather than the events
    /// themselves.
    /// </summary>
    /// <param name="unitOfWork">The unit of work holding the aggregate to commit.</param>
    /// <param name="serialize">Serializes a change into the event to store.</param>
    public async ValueTask CommitAsync(UnitOfWork unitOfWork, Func<object, SerializedEvent> serialize) {
        if (unitOfWork.GetChanged() is not { } aggregate)
            return;

        var changes = aggregate.AggregateRoot.GetChanges().ToList();
        if (changes.Count == 0)
            return;

        var identifier = aggregate.Identifier;
        var version = (long)aggregate.AggregateRoot.Version;

        var metadata = MetadataScope.Current?.Snapshot() ?? EventMetadata.Empty;
        var serializedMetadata = options.SerializeMetadata?.Invoke(metadata);

        var eventData = changes.Select((e, i) => {
            var serialized = serialize(e);
            // version is the last persisted revision; the first new event lands at version + 1,
            // the second at version + 2, etc. For a brand-new stream (version < 0) the first
            // event lands at position 0.
            var expectedPosition = version < 0 ? i : version + 1 + i;
            return new EventData(CreateEventId(identifier, expectedPosition, serialized), serialized.EventType, serialized.Data, serializedMetadata);
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
        var length = Encoding.UTF8.GetByteCount(identifier.Value) + sizeof(long) + sizeof(ulong) + Encoding.UTF8.GetByteCount(serialized.EventType);
        // Usual names fit on the stack; longer ones get a pooled buffer, with the same bytes.
        byte[]? rented = null;
        Span<byte> buffer = length <= 512 ? stackalloc byte[512] : rented = ArrayPool<byte>.Shared.Rent(length);
        try {
            var written = 0;
            written += Encoding.UTF8.GetBytes(identifier.Value, buffer[written..]);
            BinaryPrimitives.WriteInt64LittleEndian(buffer[written..], expectedPosition);
            written += sizeof(long);
            BinaryPrimitives.WriteUInt64LittleEndian(buffer[written..], XxHash64.HashToUInt64(serialized.Data.Span));
            written += sizeof(ulong);
            written += Encoding.UTF8.GetBytes(serialized.EventType, buffer[written..]);
            return Uuid.FromGuid(new Guid(MD5.HashData(buffer[..written])));
        } finally {
            if (rented is not null)
                ArrayPool<byte>.Shared.Return(rented);
        }
    }
}
