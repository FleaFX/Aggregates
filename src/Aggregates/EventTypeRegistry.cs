using System.Reflection;
using System.Runtime.CompilerServices;

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
/// old events stored under an earlier type name can still be looked up and deserialized. The
/// storage integrations upgrade them after <c>Deserialize</c> (see
/// <see cref="EventContractAttribute.Upgrade"/>); building the registry checks every upgrader.
/// </para>
/// <para>
/// Register the registry in the service collection to let subscriptions filter on event
/// type:
/// <code>
/// builder.Services.AddSingleton(registry);
/// </code>
/// Without a registered registry, every subscription receives every event type.
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
    readonly IReadOnlyList<Type> _candidates;
    readonly IReadOnlyDictionary<Type, Type> _upgradesTo;

    EventTypeRegistry(
        Dictionary<string, Type> byName, Dictionary<Type, string> byType,
        IReadOnlyList<Type> candidates, IReadOnlyDictionary<Type, Type> upgradesTo) {
        _byName = byName;
        _byType = byType;
        _candidates = candidates;
        _upgradesTo = upgradesTo;
    }

    /// <summary>
    /// Scans <paramref name="assemblies"/> for types decorated with
    /// <see cref="EventContractAttribute"/> and builds a registry from the results.
    /// </summary>
    /// <param name="assemblies">The assemblies to scan.</param>
    /// <exception cref="InvalidOperationException">An upgrader does not implement IUpgradeEvent&lt;TOld, TNew&gt; once, with TOld the type it is attached to.</exception>
    public static EventTypeRegistry FromAssemblies(params Assembly[] assemblies) =>
        FromTypes([.. assemblies.SelectMany(a => a.GetTypes())]);

    /// <summary>
    /// Builds a registry from exactly the given <paramref name="types"/>: the stored names of
    /// the types decorated with <see cref="EventContractAttribute"/>, and the type graph that
    /// <see cref="GetTypeNames"/> uses to decide which stored events a subscription can receive.
    /// </summary>
    /// <param name="types">The event types, current and historical.</param>
    /// <exception cref="InvalidOperationException">An upgrader does not implement IUpgradeEvent&lt;TOld, TNew&gt; once, with TOld the type it is attached to.</exception>
    public static EventTypeRegistry FromTypes(params Type[] types) {
        var byName = new Dictionary<string, Type>();
        var byType = new Dictionary<Type, string>();

        foreach (var type in types.Where(t => !t.IsAbstract)) {
            var attr = type.GetCustomAttribute<EventContractAttribute>();
            if (attr is null) continue;

            var name = attr.ToString();
            byName[name] = type;
            byType[type] = name;
        }

        var candidates = types
            .Where(t => !t.IsAbstract && !t.ContainsGenericParameters && !t.IsDefined(typeof(CompilerGeneratedAttribute), false))
            .Distinct()
            .ToArray();

        // Upgrade edges: an upgrader sits on the type it upgrades from. TryUpgrade takes the upgrader's
        // first IUpgradeEvent and creates the upgrader with Activator, so anything but exactly one that
        // accepts the type, or an upgrader without a parameterless constructor, fails here, at startup,
        // instead of on the first read of an old event.
        var upgradesTo = new Dictionary<Type, Type>();
        var errors = new List<string>();
        foreach (var type in candidates) {
            if (type.GetCustomAttribute<EventContractAttribute>()?.EventUpgrader is not { } upgrader)
                continue;

            var interfaces = upgrader.GetInterfaces().Where(i => i.IsGenericType && i.GetGenericTypeDefinition() == typeof(IUpgradeEvent<,>)).ToArray();
            if (interfaces is [var single] && single.GetGenericArguments()[0].IsAssignableFrom(type))
                upgradesTo[type] = single.GetGenericArguments()[1];
            else
                errors.Add($"- {type.FullName}: eventUpgrader {upgrader.Name} implements {string.Join(", ", interfaces.Select(i => $"IUpgradeEvent<{i.GetGenericArguments()[0].Name}, {i.GetGenericArguments()[1].Name}>"))}.");
            if (!upgrader.IsValueType && upgrader.GetConstructor(Type.EmptyTypes) is null)
                errors.Add($"- {type.FullName}: eventUpgrader {upgrader.Name} has no public parameterless constructor.");
        }

        if (errors.Count > 0)
            throw new InvalidOperationException($"Misconfigured event upgraders; each must implement IUpgradeEvent<TOld, TNew> once, with TOld the type it is attached to, and have a public parameterless constructor:{Environment.NewLine}{string.Join(Environment.NewLine, errors)}");

        return new EventTypeRegistry(byName, byType, candidates, upgradesTo);
    }

    /// <summary>
    /// The stored type names of every known event type that a subscription on
    /// <paramref name="eventType"/> can receive: types assignable to <paramref name="eventType"/>,
    /// and types that upgrade (directly or through a chain of upgraders) to such a type.
    /// Sorted ordinally, without duplicates. Used by subscriptions to filter on the server.
    /// </summary>
    /// <remarks>
    /// Types without <see cref="EventContractAttribute"/> count with their short CLR type name,
    /// and <c>typeof(object)</c> gives every candidate.
    /// </remarks>
    /// <param name="eventType">The event type a subscription is registered on.</param>
    public IReadOnlyList<string> GetTypeNames(Type eventType) {
        ArgumentNullException.ThrowIfNull(eventType);
        var names = new SortedSet<string>(StringComparer.Ordinal);
        foreach (var type in _candidates)
            if (CanBecome(type, eventType))
                names.Add(GetTypeName(type));
        return [.. names];
    }

    // Whether type, or a type it upgrades to (directly or through a chain), is assignable to target.
    bool CanBecome(Type type, Type target) {
        var seen = new HashSet<Type>();
        var pending = new Stack<Type>([type]);
        while (pending.TryPop(out var current)) {
            if (!seen.Add(current))
                continue;
            if (target.IsAssignableFrom(current))
                return true;
            if (_upgradesTo.TryGetValue(current, out var next))
                pending.Push(next);
        }
        return false;
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
