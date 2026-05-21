namespace Aggregates;

/// <summary>
/// Configures the event type name used when storing and reading events. Integration packages
/// use this to map between .NET types and event store type names.
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
    /// <param name="version">The version of the event contract. Defaults to 1.</param>
    /// <param name="eventUpgrader">
    /// An optional type implementing <see cref="IUpgradeEvent{TOldVersion,TNewVersion}"/> that
    /// upgrades a previous version of this event to the current version.
    /// </param>
    /// <param name="namespace">
    /// An optional namespace prepended to the contract name, separated by a dot.
    /// </param>
    public EventContractAttribute(string name, int version = 1, Type? eventUpgrader = null, string? @namespace = null) {
        if (string.IsNullOrWhiteSpace(name)) throw new ArgumentNullException(nameof(name));

        var upgrader = (
            from @interface in eventUpgrader?.GetInterfaces() ?? Enumerable.Empty<Type>()
            where @interface.IsGenericType && @interface.GetGenericTypeDefinition() == typeof(IUpgradeEvent<,>)
            let upgradeMethod = @interface.GetMethod("Upgrade")
            let instance = Activator.CreateInstance(eventUpgrader!)
            select new Func<object, object>(@event => upgradeMethod!.Invoke(instance, [@event])!)
        ).ToArray();
        if (eventUpgrader != null && !upgrader.Any()) throw new ArgumentOutOfRangeException(nameof(eventUpgrader));

        _contractName = name;
        _contractVersion = version;
        _ns = @namespace;
        _upgrader = upgrader.FirstOrDefault();
    }

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
    public bool TryUpgrade(object @event, out object upgradedEvent) {
        upgradedEvent = null!;
        if (_upgrader == null) return false;
        upgradedEvent = _upgrader(@event);
        return true;
    }

    /// <summary>
    /// Returns the fully qualified event type name as it will appear in the event store,
    /// e.g. <c>MyNamespace.ItemAdded@v2</c>.
    /// </summary>
    public override string ToString() =>
        $"{(!string.IsNullOrWhiteSpace(_ns) ? $"{_ns}." : string.Empty)}{_contractName}@v{_contractVersion}";
}
