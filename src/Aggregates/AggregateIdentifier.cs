namespace Aggregates;

/// <summary>
/// Uniquely identifies an aggregate within the system. Its value is the name of the stream the
/// aggregate (or saga) is stored in.
/// </summary>
/// <param name="Value">The string representation of the identifier: not empty, and not starting with '$'.</param>
/// <exception cref="ArgumentException">Thrown when <paramref name="Value"/> is null, empty or whitespace, or starts with '$'.</exception>
public readonly record struct AggregateIdentifier(string Value) {
    /// <summary>
    /// The string representation of the identifier.
    /// </summary>
    /// <remarks>
    /// Settable with <c>init</c>, so that serializers that create a struct with its parameterless
    /// constructor and then set its properties (System.Text.Json) keep the value; the same rules apply.
    /// </remarks>
    public string Value { get; init => field = Validate(value); } = Validate(Value);

    /// <summary>
    /// Implicitly converts a <see cref="string"/> to an <see cref="AggregateIdentifier"/>.
    /// </summary>
    public static implicit operator AggregateIdentifier(string value) => new(value);

    /// <inheritdoc/>
    public override string ToString() => Value;

    // Streams that start with '$' are reserved by the store: on KurrentDB "$$x" is the metadata of stream "x".
    static string Validate(string value) {
        ArgumentException.ThrowIfNullOrWhiteSpace(value, nameof(Value));
        if (value[0] == '$')
            throw new ArgumentException($"An aggregate or saga id can't start with '$' ('{value}'): such streams are reserved by the store.", nameof(Value));
        return value;
    }
}
