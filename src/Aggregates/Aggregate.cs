namespace Aggregates;

/// <summary>
/// Ties an <see cref="IAggregateRoot"/> to its unique <see cref="AggregateIdentifier"/> within the system.
/// </summary>
/// <param name="Identifier">Uniquely identifies the aggregate within the system.</param>
/// <param name="AggregateRoot">The root object of the aggregate.</param>
public readonly record struct Aggregate(AggregateIdentifier Identifier, IAggregateRoot AggregateRoot) {
    /// <summary>
    /// Represents the absence of an aggregate.
    /// </summary>
    public static Aggregate None => new();
}
