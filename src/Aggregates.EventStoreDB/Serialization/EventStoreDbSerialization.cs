using Aggregates.Metadata;
using Aggregates.Types;
using EventStore.Client;
using System.Reflection;
using System.Security.Cryptography;
using System.Text;
using Aggregates.EventStoreDB.Util;

namespace Aggregates.EventStoreDB.Serialization;

static class EventStoreDbSerialization {
    /// <summary>
    /// Returns a <see cref="Func{TResult}"/> that creates a <see cref="EventData"/> for each change.
    /// </summary>
    /// <param name="serializer">The <see cref="SerializerDelegate"/> to use when serializing the event payload.</param>
    /// <returns>A <see cref="Func{TResult}"/></returns>
    public static Func<Aggregate, object, EventData> CreateEntitySerializer(SerializerDelegate serializer) {
        var serializedEventsPerAggregate = new Dictionary<AggregateIdentifier, int>();

        return (aggregate, @event) => {
            // attempt to get the version offset for this aggregate
            // if we don't have one yet, initialize it to zero
            if (!serializedEventsPerAggregate.TryGetValue(aggregate.Identifier, out var versionOffset))
                serializedEventsPerAggregate[aggregate.Identifier] = versionOffset = 0;

            var eventData = new EventData(
                eventId:
                CreateEventId(aggregate, @event, versionOffset),

                type:
                GetEventType(@event),

                data:
                SerializePayload(serializer, @event),

                metadata:
                SerializePayload(serializer, GetEventMetadata(@event)));

            // next time round, the version offset for this aggregate will be incremented by one
            serializedEventsPerAggregate[aggregate.Identifier]++;

            return eventData;
        };
    }
    /// <summary>
    /// Returns a <see cref="Func{TResult}"/> that creates a <see cref="EventData"/> for each change.
    /// </summary>
    /// <param name="serializer">The <see cref="SerializerDelegate"/> to use when serializing the event payload.</param>
    /// <returns>A <see cref="Func{TResult}"/></returns>
    public static Func<Aggregate, object, EventData> CreateSagaSerializer(SerializerDelegate serializer) {
        var serializedEventsPerAggregate = new Dictionary<AggregateIdentifier, int>();

        return (aggregate, @event) => {
            // attempt to get the version offset for this aggregate
            // if we don't have one yet, initialize it to zero
            if (!serializedEventsPerAggregate.TryGetValue(aggregate.Identifier, out var versionOffset))
                serializedEventsPerAggregate[aggregate.Identifier] = versionOffset = 0;
            
            var eventData = new EventData(
                eventId:
                CreateEventId(aggregate, @event, versionOffset),

                type:
                "$>",

                data:
                Encoding.UTF8.GetBytes($"{LinkEventScope.Current!.LinkEvent.OriginalEventNumber}@{LinkEventScope.Current!.LinkEvent.OriginalStreamId}"),

                metadata:
                SerializePayload(serializer, GetEventMetadata(@event)));

            // next time round, the version offset for this aggregate will be incremented by one
            serializedEventsPerAggregate[aggregate.Identifier]++;

            return eventData;
        };
    }

    /// <summary>
    /// Creates a predictable <see cref="Guid"/> for the given <paramref name="aggregate"/>/<paramref name="event"/> combination, to be used as the unique identifier of the event.
    /// </summary>
    /// <param name="aggregate">The aggregate for which the event will be serialized.</param>
    /// <param name="event">The event that will be serialized.</param>
    /// <param name="versionOffset">The offset within the aggregate's event stream from the last persisted version.</param>
    /// <returns>A <see cref="Guid"/>.</returns>
    static Uuid CreateEventId(Aggregate aggregate, object @event, int versionOffset) {
        // event id will be a hash of aggregate id, version, event type and event hashcode, in order to ensure idempotence
        // use MD5 to compute the hash since it provides us with a 16-byte hash, which is convenient since that's the 
        // exact length of a GUID ¯\_(ツ)_/¯
        var bufferWriter = new BinaryWriter(new MemoryStream());
        bufferWriter.Write(aggregate.Identifier.ToString());
        bufferWriter.Write(aggregate.AggregateRoot.Version + versionOffset);
        bufferWriter.Write(@event.GetType().Name);
        bufferWriter.Flush();
        bufferWriter.BaseStream.Seek(0, SeekOrigin.Begin);

        var bufferReader = new BinaryReader(bufferWriter.BaseStream);
        return Uuid.FromGuid(new Guid(MD5.Create().ComputeHash(bufferReader.ReadBytes((int)bufferReader.BaseStream.Length))));
    }

    /// <summary>
    /// Gets the event type for the given <paramref name="event"/>. If the event is decorated with the <see cref="EventContractAttribute"/> attribute, that will be used to create the event type, otherwise the CLR type name is used.
    /// </summary>
    /// <param name="event">The event to get the event type of.</param>
    /// <returns>A <see cref="string"/>.</returns>
    static string GetEventType(object @event) {
        var eventContract = @event.GetType().GetCustomAttribute<EventContractAttribute>();
        return eventContract?.ToString() ?? @event.GetType().Name;
    }

    /// <summary>
    /// Gets the metadata to be saved with the event.
    /// </summary>
    /// <param name="event">The event to get the metadata for.</param>
    /// <returns>A <see cref="IDictionary{TKey,TValue}"/>.</returns>
    static IDictionary<string, object?> GetEventMetadata(object @event) {
        var scope = MetadataScope.Current;
        foreach (var metadata in @event.GetType().GetCustomAttributes<MetadataAttribute>())
            scope.Add(metadata.Create(@event));

        return scope.ToDictionary();
    }

    /// <summary>
    /// Serializes the given <paramref name="payload"/> using the given <see cref="SerializerDelegate"/>.
    /// </summary>
    /// <param name="payload">The payload to serialize.</param>
    /// <returns>A byte array.</returns>
    static byte[] SerializePayload(SerializerDelegate serialize, object payload) {
        using var stream = new MemoryStream();
        serialize(stream, payload);
        stream.Seek(0, SeekOrigin.Begin);
        var reader = new BinaryReader(stream);
        return reader.ReadBytes((int)stream.Length);
    }
}