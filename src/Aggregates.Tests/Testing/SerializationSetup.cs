using System.Reflection;
using System.Text.Json;

namespace Aggregates.Testing;

/// <summary>
/// <c>System.Text.Json</c> serialization for a fixed set of scenario event types, named after
/// their <see cref="EventContractAttribute"/> like <see cref="EventTypeRegistry"/> does.
/// </summary>
/// <remarks>
/// The set is explicit instead of scanned, so that event contracts of other tests in the same
/// assembly can't collide with the scenario's. Every <c>Deserialize</c> call is counted in the
/// <see cref="HandlerProbe"/>.
/// </remarks>
sealed class SerializationSetup {
    readonly HandlerProbe _probe;
    readonly Dictionary<string, Type> _byName = [];
    readonly Dictionary<Type, string> _byType = [];
    readonly HashSet<string> _failing = [];

    /// <summary>
    /// Creates a setup that knows <paramref name="eventTypes"/>.
    /// </summary>
    public SerializationSetup(HandlerProbe probe, IEnumerable<Type> eventTypes, IEnumerable<Type>? failingTypes = null) {
        _probe = probe;
        foreach (var type in eventTypes) {
            var name = type.GetCustomAttribute<EventContractAttribute>()?.ToString() ?? type.Name;
            _byName.Add(name, type);
            _byType.Add(type, name);
        }
        foreach (var type in failingTypes ?? [])
            _failing.Add(TypeName(type));
    }

    /// <summary>
    /// The stored event type name of <paramref name="type"/>.
    /// </summary>
    public string TypeName(Type type) =>
        _byType.TryGetValue(type, out var name) ? name : throw new InvalidOperationException($"{type} is not a scenario event type.");

    /// <summary>
    /// Serializes <paramref name="event"/> to JSON.
    /// </summary>
    public ReadOnlyMemory<byte> SerializeData(object @event) =>
        JsonSerializer.SerializeToUtf8Bytes(@event, @event.GetType());

    /// <summary>
    /// Deserializes a stored event, or returns <see langword="null"/> for an unknown event type.
    /// Throws for the event types the setup was told to fail on.
    /// </summary>
    public object? Deserialize(string eventType, ReadOnlyMemory<byte> data) {
        _probe.RecordDeserialize(eventType);
        if (_failing.Contains(eventType))
            throw new InvalidOperationException($"Deserializing {eventType} fails on purpose.");
        return _byName.TryGetValue(eventType, out var type) ? JsonSerializer.Deserialize(data.Span, type) : null;
    }

    /// <summary>
    /// Serializes <paramref name="metadata"/> as a JSON object.
    /// </summary>
    public ReadOnlyMemory<byte> SerializeMetadata(EventMetadata metadata) =>
        JsonSerializer.SerializeToUtf8Bytes(metadata.ToDictionary(entry => entry.Key, entry => entry.Value));

    /// <summary>
    /// Deserializes a JSON object written by <see cref="SerializeMetadata"/>. Arrays become
    /// <c>object?[]</c>, which is how <see cref="EventMetadata"/> represents multiple values.
    /// </summary>
    public EventMetadata DeserializeMetadata(ReadOnlyMemory<byte> data) {
        using var document = JsonDocument.Parse(data);
        return new EventMetadata(document.RootElement.EnumerateObject().ToDictionary(property => property.Name, property => ToValue(property.Value)));
    }

    static object? ToValue(JsonElement element) => element.ValueKind switch {
        JsonValueKind.String => element.GetString(),
        JsonValueKind.Number when element.TryGetInt64(out var integer) => integer,
        JsonValueKind.Number => element.GetDouble(),
        JsonValueKind.True => true,
        JsonValueKind.False => false,
        JsonValueKind.Array => element.EnumerateArray().Select(ToValue).ToArray(),
        _ => null
    };
}
