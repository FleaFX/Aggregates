namespace Aggregates.Subscriptions;

/// <summary>
/// The identity and behaviour of a saga, policy or projection: the base of their contract attributes.
/// Its string form, <c>{namespace}.{name}@v{version}</c>, is the subscription id.
/// </summary>
public abstract class SubscriptionContractAttribute : Attribute {
    readonly string _id;
    readonly string[] _predecessors;

    /// <summary>
    /// Initializes a new <see cref="SubscriptionContractAttribute"/>.
    /// </summary>
    /// <param name="name">The name of the handler.</param>
    /// <param name="version">The version of the handler; at least 1.</param>
    /// <param name="namespace">An optional namespace prepended to the name.</param>
    /// <param name="continueFrom">
    /// The subscription id of a predecessor contract whose checkpoint the handler continues from when it has
    /// none of its own. When null, every previous version from <paramref name="version"/> - 1 down to 1 is a
    /// predecessor, nearest first. An empty string (<see cref="Aggregates.Subscriptions.ContinueFrom.None"/>)
    /// means no predecessor.
    /// </param>
    /// <param name="startFromEnd">Whether the subscription starts from the end of the store when it has no checkpoint yet.</param>
    /// <param name="onError">What the subscription does with an event the handler still fails on after all retries.</param>
    /// <exception cref="ArgumentException"><paramref name="name"/> is null, empty or whitespace.</exception>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="version"/> is less than 1.</exception>
    protected SubscriptionContractAttribute(
        string name,
        int version,
        string? @namespace,
        string? continueFrom,
        bool startFromEnd,
        SubscriptionErrorMode onError
    ) {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        ArgumentOutOfRangeException.ThrowIfLessThan(version, 1);
        _id = Id(name, version, @namespace);
        _predecessors = continueFrom switch {
            "" => [],
            { } explicitId => [explicitId],
            null => [.. Enumerable.Range(1, version - 1).Reverse().Select(previous => Id(name, previous, @namespace))],
        };
        StartFromEnd = startFromEnd;
        OnError = onError;
    }

    /// <summary>
    /// The subscription ids whose checkpoint the handler continues from when it has none of its own,
    /// nearest first: <c>continueFrom</c> when given; when omitted, every previous version from
    /// <c>version</c> - 1 down to 1; empty for <c>""</c> or for version 1.
    /// </summary>
    public IReadOnlyList<string> Predecessors => _predecessors;

    /// <summary>
    /// The nearest predecessor (the first of <see cref="Predecessors"/>), or <see langword="null"/>.
    /// </summary>
    public string? ContinueFrom => _predecessors.Length > 0 ? _predecessors[0] : null;

    /// <summary>
    /// Whether the subscription starts from the end of the store when it has no checkpoint yet.
    /// </summary>
    public bool StartFromEnd { get; }

    /// <summary>
    /// What the subscription does with an event the handler still fails on after all retries. It applies to
    /// events only: deferred commands and saga deadlines are always parked, because their scheduler runs next
    /// to the subscription and serves every instance of the handler, so blocking it would hold all of them up
    /// without ordering them relative to the events.
    /// </summary>
    public SubscriptionErrorMode OnError { get; }

    /// <summary>
    /// Returns the subscription id, e.g. <c>MyNamespace.OrderFulfillment@v2</c>.
    /// </summary>
    public override string ToString() => _id;

    // {namespace}.{name}@v{version}, the namespace left out when it is empty.
    static string Id(string name, int version, string? @namespace) =>
        $"{(!string.IsNullOrWhiteSpace(@namespace) ? $"{@namespace}." : string.Empty)}{name}@v{version}";
}
