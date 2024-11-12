namespace Aggregates.Sagas;

/// <summary>
/// Reacts to an event, supported by some state built from previous events, by producing a sequence of commands to handle.
/// </summary>
/// <param name="state">The state object that supports the saga.</param>
/// <param name="event">The instigating event.</param>
/// <param name="metadata">A set of metadata that was saved with the event, if any.</param>
/// <returns>A sequence of commands.</returns>
public delegate IEnumerable<TCommand> SagaDelegate<in TSagaState, in TSagaEvent, out TCommand, TState, TEvent>(TSagaState state, TSagaEvent @event, IReadOnlyDictionary<string, object?>? metadata = null)
    where TSagaState : IState<TSagaState, TSagaEvent>
    where TState : IState<TState, TEvent>
    where TCommand : ICommand<TState, TEvent>;

/// <summary>
/// Asynchronously reacts to an event, supported by some state built from previous events, by producing a sequence of commands to handle.
/// </summary>
/// <param name="state">The state object that supports the saga.</param>
/// <param name="event">The instigating event.</param>
/// <param name="metadata">A set of metadata that was saved with the event, if any.</param>
/// <param name="cancellationToken">A <see cref="CancellationToken"/> to cancel the asynchronous enumeration.</param>
/// <returns>An asynchronous sequence of commands.</returns>
public delegate IAsyncEnumerable<TCommand> SagaAsyncDelegate<in TSagaState, in TSagaEvent, out TCommand, TState, TEvent>(TSagaState state, TSagaEvent @event, IReadOnlyDictionary<string, object?>? metadata = null, CancellationToken cancellationToken = default)
    where TSagaState : IState<TSagaState, TSagaEvent>
    where TState : IState<TState, TEvent>
    where TCommand : ICommand<TState, TEvent>;