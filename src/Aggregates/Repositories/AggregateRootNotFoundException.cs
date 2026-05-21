namespace Aggregates;

/// <summary>
/// Thrown when no aggregate exists for the given <see cref="AggregateIdentifier"/>.
/// </summary>
public class AggregateRootNotFoundException : Exception {
    /// <summary>
    /// Gets the identifier that was used to look up the aggregate.
    /// </summary>
    public AggregateIdentifier Identifier { get; }

    /// <summary>
    /// Initializes a new <see cref="AggregateRootNotFoundException"/> for <paramref name="identifier"/>.
    /// </summary>
    /// <param name="identifier">The identifier that was used to look up the aggregate.</param>
    public AggregateRootNotFoundException(AggregateIdentifier identifier) : base($"No aggregate found for '{identifier.Value}'.") =>
        Identifier = identifier;
}
