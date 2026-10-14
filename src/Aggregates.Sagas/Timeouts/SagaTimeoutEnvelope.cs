namespace Aggregates.Sagas;

/// <summary>
/// The content of a stored timeout request.
/// </summary>
/// <param name="Owner">The subscription id of the saga that requested the timeout.</param>
/// <param name="Due">When the timeout is due.</param>
/// <param name="CommandType">The stored type name of the command.</param>
/// <param name="CommandData">The serialized command.</param>
public readonly record struct SagaTimeoutEnvelope(string Owner, DateTimeOffset Due, string CommandType, ReadOnlyMemory<byte> CommandData);
