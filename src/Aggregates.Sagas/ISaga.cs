namespace Aggregates.Sagas;

/// <summary>
/// Implements the reaction logic for a saga. The implementing class may declare
/// constructor parameters — the DI container resolves them at runtime.
/// </summary>
/// <typeparam name="TSagaState">The type of the state object maintained by the saga.</typeparam>
/// <typeparam name="TEvent">The type of events this saga reacts to.</typeparam>
public interface ISaga<in TSagaState, in TEvent>
    where TSagaState : IState<TSagaState, TEvent> {
    /// <summary>
    /// Reacts to <paramref name="event"/> by producing zero or more commands to execute.
    /// The caller is responsible for dispatching the produced commands.
    /// </summary>
    /// <param name="state">The saga state after applying <paramref name="event"/>.</param>
    /// <param name="event">The triggering event.</param>
    /// <param name="cancellationToken">A cancellation token.</param>
    IAsyncEnumerable<ICommand> ReactAsync(TSagaState state, TEvent @event, CancellationToken cancellationToken = default);
}
