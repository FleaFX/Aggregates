using Aggregates.Types;
using EventStore.Client;
using System.Collections.ObjectModel;

namespace Aggregates.EventStoreDB.Serialization;

class MetadataDeserializer {
    readonly DeserializerDelegate _deserializer;

    public MetadataDeserializer(DeserializerDelegate deserializer) =>
        _deserializer = deserializer ?? throw new ArgumentNullException(nameof(deserializer));

    /// <summary>
    /// Deserializes the metadata contained in the given <paramref name="resolvedEvent"/>.
    /// </summary>
    /// <param name="resolvedEvent">The <see cref="ResolvedEvent"/> that holds all the information about this event.</param>
    /// <returns>A <see cref="IReadOnlyDictionary{TKey,TValue}"/>.</returns>
    public IReadOnlyDictionary<string, object?>? Deserialize(ResolvedEvent resolvedEvent) {
        if (resolvedEvent.Event.Metadata.IsEmpty)
            return null;

        using var stream = new MemoryStream();
        stream.Write(resolvedEvent.Event.Metadata.ToArray(), 0, resolvedEvent.Event.Metadata.Length);
        stream.Seek(0, SeekOrigin.Begin);

        return new ReadOnlyDictionary<string, object?>((Dictionary<string, object?>)_deserializer(stream, typeof(Dictionary<string, object?>)));
    }
}