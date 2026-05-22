namespace Aggregates.Sagas;

/// <summary>
/// Abstract base for the saga handler decorator chain, parallel to
/// <see cref="Aggregates.CommandHandler{TCommand}"/>.
/// </summary>
abstract class SagaHandler<TSagaState, TEvent> : ISagaHandler<TSagaState, TEvent>
    where TSagaState : IState<TSagaState, TEvent> {
    /// <inheritdoc/>
    public abstract ValueTask HandleAsync(AggregateIdentifier sagaId, TEvent @event, CancellationToken cancellationToken = default);
}

/// <summary>
/// Default saga handler. Loads or creates the <see cref="SagaRoot{TSagaState,TEvent}"/>,
/// calls <see cref="SagaRoot{TSagaState,TEvent}.AcceptAsync"/> to collect the produced commands,
/// then dispatches each command via <see cref="ICommandDispatcher"/>.
/// </summary>
class SagaHandler<TSaga, TSagaState, TEvent>(ISagaRepository<TSagaState, TEvent> repository, ISaga<TSagaState, TEvent> saga, ICommandDispatcher dispatcher)
    : SagaHandler<TSagaState, TEvent>
    where TSaga : ISaga<TSagaState, TEvent>
    where TSagaState : IState<TSagaState, TEvent> {

    /// <inheritdoc/>
    public override async ValueTask HandleAsync(AggregateIdentifier sagaId, TEvent @event, CancellationToken cancellationToken = default) {
        var sagaRoot = await repository.TryGetAsync(sagaId, cancellationToken);
        if (sagaRoot is null) {
            sagaRoot = new SagaRoot<TSagaState, TEvent>(AggregateVersion.None);
            repository.Add(sagaId, sagaRoot);
        }
        var commands = await sagaRoot.AcceptAsync(@event, saga, cancellationToken);
        foreach (var command in commands)
            await dispatcher.DispatchAsync(command, cancellationToken);
    }
}
