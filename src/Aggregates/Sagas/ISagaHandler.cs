namespace Aggregates.Sagas;

/// <summary>
/// Marker interface for saga handlers.
/// </summary>
/// <typeparam name="TSagaEvent">The type of the event(s) to react to.</typeparam>
#pragma warning disable CS1712
public interface ISagaHandler<out TSagaState, TSagaEvent, in TCommand, TCommandState, TCommandEvent>
    where TSagaState : IState<TSagaState, TSagaEvent>
    where TCommand : ICommand<TCommandState, TCommandEvent>
    where TCommandState : IState<TCommandState, TCommandEvent>
{
#pragma warning restore CS1712
    /// <summary>
    /// Asynchronously handles the given <paramref name="event"/>.
    /// </summary>
    /// <param name="delegate">The <see cref="SagaAsyncDelegate{TSagaState,TSagaEvent,TCommand,TState,TEvent}"/> that provides the reaction logic.</param>
    /// <param name="event">The event to handle.</param>
    /// <param name="metadata">The metadata associated with the event.</param>
    /// <param name="cancellationToken">A <see cref="CancellationToken"/> to cancel the asynchronous operation.</param>
    /// <returns>A <see cref="ValueTask"/> that represents the asynchronous operation.</returns>
    ValueTask HandleAsync(SagaAsyncDelegate<TSagaState, TSagaEvent, TCommand, TCommandState, TCommandEvent> @delegate, TSagaEvent @event, IReadOnlyDictionary<string, object?>? metadata, CancellationToken cancellationToken);
}