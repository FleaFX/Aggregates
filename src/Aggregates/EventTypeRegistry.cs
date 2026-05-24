using System.Reflection;

namespace Aggregates;

/// <summary>
/// A bidirectional mapping between CLR event types and the type names used to store them.
/// Build one instance at startup by scanning your event assemblies, then use it inside the
/// <c>Serialize</c> and <c>Deserialize</c> delegates on the storage integration options.
/// </summary>
/// <remarks>
/// <para>
/// For each type decorated with <see cref="EventContractAttribute"/>, the stored name is taken
/// from <see cref="EventContractAttribute.ToString()"/> (e.g. <c>Orders.ItemAdded@v2</c>).
/// Types without the attribute fall back to their short CLR type name.
/// </para>
/// <para>
/// Register all event type versions — current and historical — in the same registry so that
/// old events stored under an earlier type name can still be looked up and deserialized.
/// </para>
/// </remarks>
/// <example>
/// <code>
/// var registry = EventTypeRegistry.FromAssemblies(typeof(OrderPlaced).Assembly);
///
/// .AddKurrentDb(o => {
///     o.Serialize   = evt => new SerializedEvent(
///                         registry.GetTypeName(evt.GetType()),
///                         JsonSerializer.SerializeToUtf8Bytes(evt, evt.GetType()));
///     o.Deserialize = (typeName, data) =>
///                         registry.TryGetType(typeName, out var clrType)
///                             ? JsonSerializer.Deserialize(data.Span, clrType)
///                             : null;
/// });
/// </code>
/// </example>
public sealed class EventTypeRegistry {
    readonly IReadOnlyDictionary<string, Type> _byName;
    readonly IReadOnlyDictionary<Type, string> _byType;

    EventTypeRegistry(Dictionary<string, Type> byName, Dictionary<Type, string> byType) {
        _byName = byName;
        _byType = byType;
    }

    /// <summary>
    /// Scans <paramref name="assemblies"/> for types decorated with
    /// <see cref="EventContractAttribute"/> and builds a registry from the results.
    /// </summary>
    /// <param name="assemblies">The assemblies to scan.</param>
    public static EventTypeRegistry FromAssemblies(params Assembly[] assemblies) {
        var byName = new Dictionary<string, Type>();
        var byType = new Dictionary<Type, string>();

        foreach (var type in assemblies.SelectMany(a => a.GetTypes()).Where(t => !t.IsAbstract)) {
            var attr = type.GetCustomAttribute<EventContractAttribute>();
            if (attr is null) continue;

            var name = attr.ToString();
            byName[name] = type;
            byType[type] = name;
        }

        return new EventTypeRegistry(byName, byType);
    }

    /// <summary>
    /// Returns the stored type name for <paramref name="type"/>. When <paramref name="type"/>
    /// is decorated with <see cref="EventContractAttribute"/>, its
    /// <see cref="EventContractAttribute.ToString()"/> value is returned; otherwise the CLR
    /// type's short name is used as a fallback.
    /// </summary>
    /// <param name="type">The CLR event type.</param>
    public string GetTypeName(Type type) =>
        _byType.TryGetValue(type, out var name) ? name : type.Name;

    /// <summary>
    /// Returns the stored type name for the runtime type of <paramref name="event"/>.
    /// Equivalent to calling <c>GetTypeName(@event.GetType())</c>.
    /// </summary>
    /// <typeparam name="TEvent">The declared event type.</typeparam>
    /// <param name="event">The event instance whose runtime type is looked up.</param>
    public string GetTypeName<TEvent>(TEvent @event) =>
        GetTypeName(@event!.GetType());

    /// <summary>
    /// Looks up the CLR type for the given stored <paramref name="typeName"/>.
    /// </summary>
    /// <param name="typeName">The type name as stored in the event store.</param>
    /// <param name="type">
    /// When this method returns <see langword="true"/>, contains the CLR type; otherwise
    /// <see langword="null"/>.
    /// </param>
    /// <returns>
    /// <see langword="true"/> when a matching type was found; <see langword="false"/> when the
    /// type name is unknown (e.g. an event type from another service or a removed event).
    /// </returns>
    public bool TryGetType(string typeName, out Type type) =>
        _byName.TryGetValue(typeName, out type!);
}
