namespace Aggregates;

/// <summary>
/// Marker interface for all commands.
/// </summary>
public interface ICommand;

/// <summary>
/// Produces a sequence of events that progress a state object from one version to the next.
/// </summary>
/// <typeparam name="TState">The type of the state this command operates on.</typeparam>
/// <typeparam name="TEvent">The type of events this command produces.</typeparam>
public interface ICommand<in TState, out TEvent> : ICommand
    where TState : IState<TState, TEvent> {
    /// <summary>
    /// Gets the identifier of the aggregate this command targets.
    /// </summary>
    AggregateIdentifier Id { get; }

    /// <summary>
    /// Accepts the current <paramref name="state"/> and asynchronously produces the events
    /// needed to progress it to the next state.
    /// </summary>
    /// <param name="state">The current state of the aggregate.</param>
    /// <param name="cancellationToken">A cancellation token.</param>
    /// <returns>An asynchronous sequence of events.</returns>
    IAsyncEnumerable<TEvent> ProgressAsync(TState state, CancellationToken cancellationToken = default);
}
