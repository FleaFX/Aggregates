namespace Aggregates.Sagas;

/// <summary>
/// Handles an incoming event for a specific saga instance. Integration packages
/// subscribe to event streams, extract the saga identifier from event metadata, and
/// call this for each incoming event.
/// </summary>
/// <typeparam name="TSagaState">The type of the state object maintained by the saga.</typeparam>
/// <typeparam name="TEvent">The type of events this handler processes.</typeparam>
public interface ISagaHandler<TSagaState, in TEvent>
    where TSagaState : IState<TSagaState, TEvent> {
    /// <summary>
    /// Handles <paramref name="event"/> for the saga identified by <paramref name="sagaId"/>.
    /// </summary>
    /// <param name="sagaId">Identifies the saga instance to update.</param>
    /// <param name="event">The event to handle.</param>
    /// <param name="cancellationToken">A cancellation token.</param>
    ValueTask HandleAsync(AggregateIdentifier sagaId, TEvent @event, CancellationToken cancellationToken = default);
}
