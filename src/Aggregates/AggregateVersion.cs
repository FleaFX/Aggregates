namespace Aggregates;

/// <summary>
/// Tracks the version of an aggregate, used for optimistic concurrency control.
/// </summary>
public readonly struct AggregateVersion {
    readonly long _value;

    /// <summary>
    /// Represents an aggregate with no prior history (i.e. a new aggregate).
    /// </summary>
    public static AggregateVersion None => new(long.MinValue);

    /// <summary>
    /// Initializes a new <see cref="AggregateVersion"/> with the given <paramref name="value"/>.
    /// Negative values are treated as <see cref="None"/>.
    /// </summary>
    /// <param name="value">The version number.</param>
    public AggregateVersion(long value) => _value = value >= 0 ? value : long.MinValue;

    /// <summary>
    /// Implicitly converts an <see cref="AggregateVersion"/> to a <see cref="long"/>.
    /// </summary>
    public static implicit operator long(AggregateVersion instance) => instance._value;
}
