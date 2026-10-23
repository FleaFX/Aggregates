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
/// Types without the attribute are stored under their full type name (<c>Orders.Events.ItemAdded</c>):
/// the class name is then the contract, so renaming or moving the class makes its stored events
/// unreadable; use <see cref="EventContractAttribute"/> for events that are kept.
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
    /// <exception cref="InvalidOperationException">Two event types with an EventContractAttribute share a stored name, a closed generic event type has no EventContractAttribute, or an upgrader does not implement IUpgradeEvent&lt;TOld, TNew&gt; once, with TOld the type it is attached to.</exception>
    public static EventTypeRegistry FromAssemblies(params Assembly[] assemblies) =>
        FromTypes([.. assemblies.SelectMany(a => a.GetTypes())]);

    /// <summary>
    /// Builds a registry from exactly the given <paramref name="types"/>: the stored names of
    /// the types decorated with <see cref="EventContractAttribute"/>, and the type graph that
    /// <see cref="GetTypeNames"/> uses to decide which stored events a subscription can receive.
    /// </summary>
    /// <param name="types">The event types, current and historical.</param>
    /// <exception cref="InvalidOperationException">Two event types with an EventContractAttribute share a stored name, a closed generic event type has no EventContractAttribute, or an upgrader does not implement IUpgradeEvent&lt;TOld, TNew&gt; once, with TOld the type it is attached to.</exception>
    public static EventTypeRegistry FromTypes(params Type[] types) {
        var candidates = types
            .Where(t => !t.IsAbstract && !t.ContainsGenericParameters && !t.IsDefined(typeof(CompilerGeneratedAttribute), false))
            .Distinct()
            .ToArray();

        // The stored name of every candidate: its contract, or its full type name when it has none — the class
        // name is the contract then, so renaming or moving the class makes its stored events unreadable. A
        // closed generic type without a contract is refused: its full name holds the assembly versions of its
        // type arguments. Two types may not share a contract name, or old events could come back as the wrong
        // type; types without a contract that share a full name are left out: the name is ambiguous, and the
        // warning at commit reports an event among them.
        var byName = new Dictionary<string, Type>();
        var byType = new Dictionary<Type, string>();
        var typesByName = new Dictionary<string, List<Type>>();
        var nameErrors = new List<string>();
        foreach (var type in candidates) {
            var attr = type.GetCustomAttribute<EventContractAttribute>();
            if (attr is null && type.IsGenericType) {
                nameErrors.Add($"- {type}: a generic event type needs an EventContractAttribute; its full name holds the versions of its type arguments.");
                continue;
            }
            var name = attr?.ToString() ?? type.FullName!;
            if (!typesByName.TryGetValue(name, out var named))
                typesByName[name] = named = [];
            named.Add(type);
        }
        foreach (var (name, named) in typesByName.OrderBy(entry => entry.Key, StringComparer.Ordinal)) {
            // A contract name and a full type name can't collide: a contract ends on @v{version}, a full name holds no @.
            if (named.Count > 1 && named[0].GetCustomAttribute<EventContractAttribute>() is not null) {
                nameErrors.Add($"- '{name}': {string.Join(", ", named.Select(type => type.FullName))}");
                continue;
            }
            if (named.Count > 1)
                continue;
            byName[name] = named[0];
            byType[named[0]] = name;
        }
        if (nameErrors.Count > 0)
            throw new InvalidOperationException($"Event types can't share a contract name, and a generic event type needs an EventContractAttribute:{Environment.NewLine}{string.Join(Environment.NewLine, nameErrors)}");

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
    /// Types without <see cref="EventContractAttribute"/> count with their full type name,
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
    /// <see cref="EventContractAttribute.ToString()"/> value is returned; otherwise its full
    /// type name is used, which makes renaming or moving the class break its stored events.
    /// </summary>
    /// <param name="type">The CLR event type.</param>
    /// <exception cref="InvalidOperationException">
    /// <paramref name="type"/> is a generic type without <see cref="EventContractAttribute"/>:
    /// its full name holds the versions of its type arguments.
    /// </exception>
    public string GetTypeName(Type type) {
        if (_byType.TryGetValue(type, out var name))
            return name;
        var attr = type.GetCustomAttribute<EventContractAttribute>();
        if (attr is not null)
            return attr.ToString();
        if (type.IsGenericType)
            throw new InvalidOperationException(
                $"{type}: a generic event type needs an EventContractAttribute; its full name holds the versions of its type arguments.");
        return type.FullName!;
    }

    /// <summary>
    /// Returns the stored type name for the runtime type of <paramref name="event"/>.
    /// Equivalent to calling <c>GetTypeName(@event.GetType())</c>.
    /// </summary>
    /// <typeparam name="TEvent">The declared event type.</typeparam>
    /// <param name="event">The event instance whose runtime type is looked up.</param>
    public string GetTypeName<TEvent>(TEvent @event) =>
        GetTypeName(@event!.GetType());

    /// <summary>
    /// Looks up the CLR type for the given stored <paramref name="typeName"/>: the type with
    /// <see cref="EventContractAttribute"/> for that name, or the type whose full type name it is.
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
