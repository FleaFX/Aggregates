using Aggregates.Types;

namespace Aggregates;

/// <summary>
/// Ties the <paramref name="AggregateRoot"/> to its unique <paramref name="Identifier"/> within the system.
/// </summary>
/// <param name="Identifier">Uniquely identifies the aggregate within the system.</param>
/// <param name="AggregateRoot">The root object of the aggregate.</param>
readonly record struct Aggregate(AggregateIdentifier Identifier, IAggregateRoot AggregateRoot) {
    /// <summary>
    /// Used to check whether an aggregate was loaded.
    /// </summary>
    public static Aggregate None => new();
}