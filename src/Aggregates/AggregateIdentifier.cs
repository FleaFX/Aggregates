namespace Aggregates;

/// <summary>
/// Uniquely identifies an aggregate within the system.
/// </summary>
/// <param name="Value">The string representation of the identifier.</param>
public record struct AggregateIdentifier(string Value) {
    /// <summary>
    /// Implicitly converts a <see cref="string"/> to an <see cref="AggregateIdentifier"/>.
    /// </summary>
    public static implicit operator AggregateIdentifier(string value) => new(value);

    /// <inheritdoc/>
    public override string ToString() => Value;
}
