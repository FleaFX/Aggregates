namespace Aggregates.Types;

/// <summary>
/// Serializes the given <paramref name="payload"/> to the given <paramref name="destination"/> <see cref="Stream"/>.
/// </summary>
/// <param name="destination">The <see cref="Stream"/> to write the serialized payload to.</param>
/// <param name="payload">The payload to serialize.</param>
public delegate void SerializerDelegate(Stream destination, object payload);

/// <summary>
/// Deserializes the given binary serialized payload.
/// </summary>
/// <param name="source">The <see cref="Stream"/> to read the serialized payload from.</param>
/// <param name="target">The target type to deserialize to.</param>
/// <returns>A <see cref="object"/>.</returns>
public delegate object DeserializerDelegate(Stream source, Type target);