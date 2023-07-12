namespace Aggregates.Types;

/// <summary>
/// Provides an origin and a function to apply an event to the current state.
/// </summary>
/// <typeparam name="TState">The type of the maintained state object.</typeparam>
/// <typeparam name="TEvent">The type of the event(s) that are applicable.</typeparam>
public interface IState<out TState, in TEvent> where TState : IState<TState, TEvent> {
    /// <summary>
    /// Gets the initial state.
    /// </summary>
    static abstract TState Initial { get; }

    /// <summary>
    /// Applies the given <paramref name="event"/> to progress to a new state.
    /// </summary>
    /// <param name="event">The event to apply.</param>
    /// <returns>The new state.</returns>
    TState Apply(TEvent @event);
}