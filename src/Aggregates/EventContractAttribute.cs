using System.Diagnostics.CodeAnalysis;
using System.Reflection;

namespace Aggregates;

/// <summary>
/// Configures the event type name used when storing and reading events. Integration packages
/// use this to map between .NET types and event store type names.
/// </summary>
[AttributeUsage(AttributeTargets.Class | AttributeTargets.Struct, AllowMultiple = false, Inherited = false)]
public sealed class EventContractAttribute : Attribute {
    readonly string _contractName;
    readonly int _contractVersion;
    readonly string? _ns;
    readonly Lazy<Func<object, object>>? _upgrader;

    /// <summary>
    /// Initializes a new <see cref="EventContractAttribute"/>.
    /// </summary>
    /// <param name="name">The name of the event contract.</param>
    /// <param name="version">The version of the event contract. Defaults to 1. At least 1.</param>
    /// <param name="eventUpgrader">
    /// An optional type implementing <see cref="IUpgradeEvent{TOldVersion,TNewVersion}"/> that
    /// upgrades this version of the event to the next one: put it on the old version, with the
    /// attributed type as <c>TOldVersion</c>, with a public parameterless constructor. See <see cref="Upgrade"/>.
    /// </param>
    /// <param name="namespace">
    /// An optional namespace prepended to the contract name, separated by a dot.
    /// </param>
    /// <exception cref="ArgumentException"><paramref name="name"/> is null, empty or whitespace.</exception>
    /// <exception cref="ArgumentOutOfRangeException">
    /// <paramref name="version"/> is less than 1, or <paramref name="eventUpgrader"/> implements no
    /// <see cref="IUpgradeEvent{TOldVersion,TNewVersion}"/>.
    /// </exception>
    public EventContractAttribute(string name, int version = 1, Type? eventUpgrader = null, string? @namespace = null) {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        ArgumentOutOfRangeException.ThrowIfLessThan(version, 1);

        var upgradeMethod = (
            from @interface in eventUpgrader?.GetInterfaces() ?? Enumerable.Empty<Type>()
            where @interface.IsGenericType && @interface.GetGenericTypeDefinition() == typeof(IUpgradeEvent<,>)
            select @interface.GetMethod("Upgrade")
        ).FirstOrDefault();
        if (eventUpgrader != null && upgradeMethod is null)
            throw new ArgumentOutOfRangeException(nameof(eventUpgrader), eventUpgrader, $"{eventUpgrader.Name} doesn't implement IUpgradeEvent<TOldVersion, TNewVersion>.");

        _contractName = name;
        _contractVersion = version;
        _ns = @namespace;
        // Created on first use, so reading the attribute never fails on the upgrader; EventTypeRegistry checks it at startup.
        _upgrader = upgradeMethod is null ? null : new Lazy<Func<object, object>>(() => {
            var instance = Activator.CreateInstance(eventUpgrader!);
            return @event => upgradeMethod.Invoke(instance, BindingFlags.DoNotWrapExceptions, binder: null, [@event], culture: null)!;
        });
        EventUpgrader = eventUpgrader;
    }

    /// <summary>
    /// The type passed as <c>eventUpgrader</c>, or null.
    /// </summary>
    public Type? EventUpgrader { get; }

    /// <summary>
    /// Attempts to upgrade the given <paramref name="event"/> using the configured
    /// <see cref="IUpgradeEvent{TOldVersion,TNewVersion}"/>.
    /// </summary>
    /// <param name="event">The event to upgrade.</param>
    /// <param name="upgradedEvent">The resulting upgraded event, if upgrade succeeded.</param>
    /// <returns>
    /// <see langword="true"/> if the event was upgraded; <see langword="false"/> if no upgrader
    /// was configured.
    /// </returns>
    internal bool TryUpgrade(object @event, out object upgradedEvent) {
        upgradedEvent = null!;
        if (_upgrader == null) return false;
        upgradedEvent = _upgrader.Value(@event);
        return true;
    }

    /// <summary>
    /// Upgrades <paramref name="event"/> with the upgrader on its type, then the result with the
    /// upgrader on its type, and so on, until a type has none. The storage integrations apply this
    /// to every event their <c>Deserialize</c> returns.
    /// </summary>
    /// <param name="event">A deserialized event, or <see langword="null"/>.</param>
    /// <returns>The last version of the event, or <see langword="null"/> when <paramref name="event"/> is <see langword="null"/>.</returns>
    [return: NotNullIfNotNull(nameof(@event))]
    public static object? Upgrade(object? @event) {
        while (@event?.GetType().GetCustomAttribute<EventContractAttribute>() is { } contract && contract.TryUpgrade(@event, out var upgraded))
            @event = upgraded;
        return @event;
    }

    /// <summary>
    /// Returns the fully qualified event type name as it will appear in the event store,
    /// e.g. <c>MyNamespace.ItemAdded@v2</c>.
    /// </summary>
    public override string ToString() =>
        $"{(!string.IsNullOrWhiteSpace(_ns) ? $"{_ns}." : string.Empty)}{_contractName}@v{_contractVersion}";
}
