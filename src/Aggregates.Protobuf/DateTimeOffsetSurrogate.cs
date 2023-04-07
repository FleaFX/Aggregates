using ProtoBuf;

namespace Aggregates.Protobuf;

[ProtoContract]
record struct DateTimeOffsetSurrogate(
    [property: ProtoMember(1)] long DateTimeTicks,
    [property: ProtoMember(2)] short OffsetMinutes) {
    /// <summary>
    /// Implicitly casts a <see cref="DateTimeOffset"/> <see cref="DateTimeOffsetSurrogate"/>.
    /// </summary>
    /// <param name="dateTimeOffset">The <see cref="DateTimeOffset"/> to cast.</param>
    public static implicit operator DateTimeOffsetSurrogate(DateTimeOffset dateTimeOffset) =>
        new(dateTimeOffset.Ticks, (short)dateTimeOffset.Offset.TotalMinutes);

    /// <summary>
    /// Implicitly casts a <see cref="DateTimeOffsetSurrogate"/> to a <see cref="DateTimeOffset"/>.
    /// </summary>
    /// <param name="surrogate">The <see cref="DateTimeOffsetSurrogate"/> to cast.</param>
    public static implicit operator DateTimeOffset(DateTimeOffsetSurrogate surrogate) =>
        new(surrogate.DateTimeTicks, TimeSpan.FromMinutes(surrogate.OffsetMinutes));
}