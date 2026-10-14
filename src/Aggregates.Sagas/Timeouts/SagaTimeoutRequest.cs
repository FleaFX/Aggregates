namespace Aggregates.Sagas;

/// <summary>
/// A timeout requested by a saga, recorded as a change of its <see cref="SagaRoot{TSagaState,TEvent}"/>;
/// the saga commit stores it as a <see cref="SagaTimeoutFormat.RequestEventType"/> entry.
/// </summary>
/// <param name="Owner">The subscription id of the saga that requested it.</param>
/// <param name="Due">When the timeout is due.</param>
/// <param name="Command">The command to execute.</param>
public readonly record struct SagaTimeoutRequest(string Owner, DateTimeOffset Due, ICommand Command);
