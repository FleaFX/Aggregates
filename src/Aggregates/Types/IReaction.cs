using System.Runtime.CompilerServices;

namespace Aggregates.Types;

/// <summary>
/// Reacts to an event by producing new commands to handle.
/// </summary>
/// <typeparam name="TReactionEvent">The type of the event to react to.</typeparam>
/// <typeparam name="TCommand">The type of the produced commands.</typeparam>
/// <typeparam name="TState">The type of the state object that is acted upon by the produced commands.</typeparam>
/// <typeparam name="TEvent">The type of the event(s) that are applicable to the state that is acted upon by the produced commands.</typeparam>
public interface IReaction<in TReactionEvent, out TCommand, TState, TEvent>
    where TState : IState<TState, TEvent>
    where TCommand : ICommand<TCommand, TState, TEvent> {
    /// <summary>
    /// Asynchronously reacts to an event by producing a sequence of commands to handle.
    /// </summary>
    /// <param name="event">The instigating event.</param>
    /// <param name="metadata">A set of metadata that was saved with the event, if any.</param>
    /// <returns>A sequence of commands.</returns>
    IEnumerable<TCommand> React(TReactionEvent @event, IReadOnlyDictionary<string, object?>? metadata = null) =>
        ReactAsync(@event, metadata).ToEnumerable();

    /// <summary>
    /// Asynchronously reacts to an event by producing a sequence of commands to handle.
    /// </summary>
    /// <param name="event">The instigating event.</param>
    /// <param name="metadata">A set of metadata that was saved with the event, if any.</param>
    /// <param name="cancellationToken">A <see cref="CancellationToken"/> to cancel the asynchronous enumeration.</param>
    /// <returns>An asynchronous sequence of commands.</returns>
    IAsyncEnumerable<TCommand> ReactAsync(TReactionEvent @event, IReadOnlyDictionary<string, object?>? metadata = null, CancellationToken cancellationToken = default) =>
        React(@event, metadata).ToAsyncEnumerable();
}

/// <summary>
/// Reacts to an event by producing new commands to handle. This interface includes a state object to support the reaction.
/// </summary>
/// <typeparam name="TReactionState">The type of the state object that supports the reaction logic.</typeparam>
/// <typeparam name="TReactionEvent">The type of the event to react to.</typeparam>
/// <typeparam name="TCommand">The type of the produced commands.</typeparam>
/// <typeparam name="TState">The type of the state object that is acted upon by the produced commands.</typeparam>
/// <typeparam name="TEvent">The type of the event(s) that are applicable to the state that is acted upon by the produced commands.</typeparam>
public interface IReaction<in TReactionState, in TReactionEvent, out TCommand, TState, TEvent>
    where TReactionState : IState<TReactionState, TReactionEvent>
    where TState : IState<TState, TEvent>
    where TCommand : ICommand<TCommand, TState, TEvent> {
    /// <summary>
    /// Asynchronously reacts to an event by producing a sequence of commands to handle.
    /// </summary>
    /// <param name="state">The state object that supports the reaction.</param>
    /// <param name="event">The instigating event.</param>
    /// <param name="metadata">A set of metadata that was saved with the event, if any.</param>
    /// <returns>A sequence of commands.</returns>
    IEnumerable<TCommand> React(TReactionState state, TReactionEvent @event, IReadOnlyDictionary<string, object?>? metadata = null) =>
        ReactAsync(state, @event, metadata).ToEnumerable();

    /// <summary>
    /// Asynchronously reacts to an event by producing a sequence of commands to handle.
    /// </summary>
    /// <param name="state">The state object that supports the reaction.</param>
    /// <param name="event">The instigating event.</param>
    /// <param name="metadata">A set of metadata that was saved with the event, if any.</param>
    /// <param name="cancellationToken">A <see cref="CancellationToken"/> to cancel the asynchronous enumeration.</param>
    /// <returns>An asynchronous sequence of commands.</returns>
    async IAsyncEnumerable<TCommand> ReactAsync(TReactionState state, TReactionEvent @event, IReadOnlyDictionary<string, object?>? metadata = null, [EnumeratorCancellation] CancellationToken cancellationToken = default) {
        await foreach (var command in React(state, @event, metadata).ToAsyncEnumerable().WithCancellation(cancellationToken)) {
            yield return command;
        }
    }
}