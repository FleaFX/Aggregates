namespace Aggregates.Types;

/// <summary>
/// Uniquely identifies an aggregate within the system.
/// </summary>
/// <param name="Value">The string representation of the identifier.</param>
public record struct AggregateIdentifier(string Value) {
    /// <summary>
    /// Casts the given <see cref="string"/> to a <see cref="AggregateIdentifier"/>.
    /// </summary>
    /// <param name="value">The <see cref="string"/> to cast.</param>
    public static implicit operator AggregateIdentifier(string value) => new(value);
}