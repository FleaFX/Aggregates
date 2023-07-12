namespace Aggregates.Types;

/// <summary>
/// Configures the event type when storing.
/// </summary>
[AttributeUsage(AttributeTargets.Class | AttributeTargets.Struct, AllowMultiple = false, Inherited = false)]
public class EventContractAttribute : Attribute {
    readonly string _contractName;
    readonly int _contractVersion;
    readonly string? _ns;
    readonly Func<object, object>? _upgrader;

    /// <summary>
    /// Initializes a new <see cref="EventContractAttribute"/>.
    /// </summary>
    /// <param name="name">The name of the event contract.</param>
    /// <param name="version">The version of the event contract.</param>
    /// <param name="eventUpgrader">A type that implements <see cref="IUpgradeEvent{TOldVersion,TNewVersion}"/> which is to be used to upgrade the attributed event type.</param>
    /// <param name="namespace">An optional namespace to prepend to the contract name. The namespace will be separated from the contract name with a dot.</param>
    public EventContractAttribute(string name, int version = 1, Type? eventUpgrader = null, string? @namespace = null) {
        if (string.IsNullOrWhiteSpace(name)) throw new ArgumentNullException(nameof(name));

        // validate that the eventUpgrader is an implementation of IUpgradeEvent
        var upgrader = (
            from iface in eventUpgrader?.GetInterfaces() ?? Enumerable.Empty<Type>()
            where iface.IsGenericType && iface.GetGenericTypeDefinition() == typeof(IUpgradeEvent<,>)
            let upgradeMethod = iface.GetMethod("Upgrade")
            let instance = Activator.CreateInstance(eventUpgrader!)
            select new Func<object, object>(@event => upgradeMethod!.Invoke(instance, new[] { @event })!)
        ).ToArray();
        if (eventUpgrader != null && !upgrader.Any()) throw new ArgumentOutOfRangeException(nameof(eventUpgrader));

        _contractName = name;
        _contractVersion = version;
        _ns = @namespace;
        _upgrader = upgrader.FirstOrDefault();
    }

    /// <summary>
    /// Attempts to upgrade the given <paramref name="event"/>.
    /// </summary>
    /// <remarks>A given <paramref name="event"/> can only be upgraded if an <see cref="IUpgradeEvent{TOldVersion,TNewVersion}"/> type was passed to the constructor.</remarks>
    /// <param name="event">The event to upgrade.</param>
    /// <param name="upgradedEvent">The resulting upgraded event.</param>
    /// <returns><c>true</c> if the given <paramref name="event"/> was upgraded, otherwise <c>false</c>.</returns>
    public bool TryUpgrade(object @event, out object upgradedEvent) {
        upgradedEvent = default!;
        if (_upgrader == default) return false;
        upgradedEvent = _upgrader(@event);
        return true;
    }

    /// <summary>
    /// Returns the event contract name as it will be used in the event log.
    /// </summary>
    /// <returns>A <see cref="string"/>.</returns>
    public override string ToString() => $"{(!string.IsNullOrWhiteSpace(_ns) ? $"{_ns}." : string.Empty)}{_contractName}@v{_contractVersion}";
}