namespace Aggregates.Policies;

/// <summary>
/// Reacts to an event by producing a sequence of commands to handle.
/// </summary>
/// <param name="event">The instigating event.</param>
/// <param name="metadata">A set of metadata that was saved with the event, if any.</param>
/// <returns>A sequence of commands.</returns>
public delegate IEnumerable<TCommand> PolicyDelegate<in TPolicyEvent, out TCommand, TState, TEvent>(TPolicyEvent @event, IReadOnlyDictionary<string, object?>? metadata = null)
    where TState : IState<TState, TEvent>
    where TCommand : ICommand<TState, TEvent>;

/// <summary>
/// Asynchronously reacts to an event by producing a sequence of commands to handle.
/// </summary>
/// <param name="event">The instigating event.</param>
/// <param name="metadata">A set of metadata that was saved with the event, if any.</param>
/// <param name="cancellationToken">A <see cref="CancellationToken"/> to cancel the asynchronous enumeration.</param>
/// <returns>An asynchronous sequence of commands.</returns>
public delegate IAsyncEnumerable<TCommand> PolicyAsyncDelegate<in TPolicyEvent, out TCommand, TState, TEvent>(TPolicyEvent @event, IReadOnlyDictionary<string, object?>? metadata = null, CancellationToken cancellationToken = default)
    where TState : IState<TState, TEvent>
    where TCommand : ICommand<TState, TEvent>;