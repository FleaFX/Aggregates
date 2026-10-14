using Aggregates.Subscriptions;

namespace Aggregates.Sagas;

/// <summary>
/// Default saga handler and innermost link of the saga handler chain. Loads or creates the
/// <see cref="SagaRoot{TSagaState,TEvent}"/>, calls <see cref="SagaRoot{TSagaState,TEvent}.AcceptAsync"/>
/// with <typeparamref name="TSaga"/> to collect the produced commands, then dispatches each command
/// via <see cref="ICommandDispatcher"/>. It activates the saga's timeout scheduler when the saga
/// plans its first command.
/// </summary>
/// <typeparam name="TSaga">The saga class.</typeparam>
/// <typeparam name="TSagaState">The saga state type.</typeparam>
/// <typeparam name="TEvent">The event type the saga reacts to.</typeparam>
sealed class SagaHandler<TSaga, TSagaState, TEvent>(ISagaRepository<TSagaState, TEvent> repository, TSaga saga, ICommandDispatcher dispatcher, SagaTimeoutActivation<TSaga> activation)
    where TSaga : ISaga<TSagaState, TEvent>
    where TSagaState : IState<TSagaState, TEvent> {

    /// <summary>
    /// Handles <paramref name="event"/> for the saga identified by <paramref name="sagaId"/>.
    /// </summary>
    /// <param name="sagaId">Identifies the saga instance to update.</param>
    /// <param name="event">The event to handle.</param>
    /// <param name="source">Identifies the stored event that triggered the saga.</param>
    /// <param name="cancellationToken">A cancellation token.</param>
    public async ValueTask HandleAsync(AggregateIdentifier sagaId, TEvent @event, EventReference source, CancellationToken cancellationToken = default) {
        var sagaRoot = await repository.TryGetAsync(sagaId, cancellationToken);
        if (sagaRoot is null) {
            sagaRoot = new SagaRoot<TSagaState, TEvent>(default, AggregateVersion.None);
            repository.Add(sagaId, sagaRoot);
        }
        var commands = await sagaRoot.AcceptAsync(@event, source, saga, cancellationToken);
        // A saga's first timeout request activates its scheduler before the request is committed (after
        // this handler returns), so the scheduler never misses it.
        if (sagaRoot.GetChanges().Any(change => change is SagaTimeoutRequest))
            await activation.EnsureActivatedAsync(cancellationToken);
        foreach (var command in commands)
            await dispatcher.DispatchAsync(command, cancellationToken);
    }
}
