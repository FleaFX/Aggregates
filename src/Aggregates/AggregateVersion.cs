namespace Aggregates;

/// <summary>
/// Tracks the version of an aggregate.
/// </summary>
readonly struct AggregateVersion {
    readonly long _value;

    /// <summary>
    /// Version used when the aggregate has no prior existing history.
    /// </summary>
    public static AggregateVersion None => new(long.MinValue);

    /// <summary>
    /// Initializes a new <see cref="AggregateVersion"/>.
    /// </summary>
    /// <param name="value"></param>
    public AggregateVersion(long value) => _value = value >= 0 ? value : long.MinValue;

    /// <summary>
    /// Implicitly casts the given <paramref name="instance"/> to a <see cref="long"/>.
    /// </summary>
    /// <param name="instance">The <see cref="AggregateVersion"/> to cast.</param>
    public static implicit operator long(AggregateVersion instance) => instance._value;
}