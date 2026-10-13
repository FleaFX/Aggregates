namespace Aggregates.Sagas;

/// <summary>
/// Configures the identity and behaviour of a saga.
/// </summary>
/// <param name="name">The name of the saga.</param>
/// <param name="version">The version of the saga. Defaults to 1.</param>
/// <param name="namespace">An optional namespace prepended to the saga name.</param>
/// <param name="continueFrom">
/// The subscription id of a predecessor contract whose checkpoint this saga continues from
/// when it has none of its own. When omitted, every previous version from
/// <paramref name="version"/> - 1 down to 1 is a predecessor, nearest first. An empty string
/// (<see cref="Aggregates.Subscriptions.ContinueFrom.None"/>) means no predecessor: the
/// subscription starts from the beginning, or from the end with
/// <paramref name="startFromEnd"/>.
/// </param>
/// <param name="startFromEnd">
/// When <see langword="true"/>, the saga starts processing from the end of the stream
/// rather than the beginning.
/// </param>
[AttributeUsage(AttributeTargets.Class | AttributeTargets.Struct, Inherited = false)]
public sealed class SagaContractAttribute(
    string name,
    int version = 1,
    string? @namespace = null,
    string? continueFrom = null,
    bool startFromEnd = false) : Attribute {

    readonly string[] _predecessors = continueFrom switch {
        "" => [],
        { } explicitId => [explicitId],
        null => [.. Enumerable.Range(1, Math.Max(version - 1, 0)).Reverse()
            .Select(previous => new SagaContractAttribute(name, previous, @namespace).ToString())],
    };

    /// <summary>
    /// The subscription ids whose checkpoint this saga continues from when it has none of
    /// its own, nearest first: <c>continueFrom</c> when given; when omitted, every
    /// previous version from <c>version</c> - 1 down to 1; empty for
    /// <c>""</c> or for version 1.
    /// </summary>
    public IReadOnlyList<string> Predecessors => _predecessors;

    /// <summary>
    /// The nearest predecessor (the first of <see cref="Predecessors"/>), or
    /// <see langword="null"/>.
    /// </summary>
    public string? ContinueFrom => _predecessors.Length > 0 ? _predecessors[0] : null;

    /// <summary>
    /// Indicates whether the saga should start from the end of the stream.
    /// </summary>
    public bool StartFromEnd => startFromEnd;

    /// <summary>
    /// Returns the fully qualified saga name, e.g. <c>MyNamespace.OrderFulfillment@v2</c>.
    /// </summary>
    public override string ToString() =>
        $"{(!string.IsNullOrWhiteSpace(@namespace) ? $"{@namespace}." : string.Empty)}{name}@v{version}";
}
