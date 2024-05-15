namespace Aggregates.Sagas;

/// <summary>
/// Describes how a saga can be identified.
/// </summary>
/// <param name="EventType">The type of the handled event.</param>
/// <param name="SagaId">Uniquely identifies the saga within the system.</param>
public sealed record SagaMetadata(string EventType, string SagaId);