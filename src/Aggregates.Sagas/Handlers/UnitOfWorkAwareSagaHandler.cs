namespace Aggregates.Sagas;

/// <summary>
/// Decorates a <see cref="SagaHandler{TSagaState,TEvent}"/> with <see cref="UnitOfWork"/>
/// lifecycle management, analogous to
/// <see cref="Aggregates.UnitOfWorkAwareCommandHandler{TCommand}"/>.
/// Creates a fresh <see cref="UnitOfWork"/> and <see cref="UnitOfWorkScope"/> for each event,
/// making the unit of work available to repositories via the ambient scope.
/// </summary>
sealed class UnitOfWorkAwareSagaHandler<TSagaState, TEvent>(SagaHandler<TSagaState, TEvent> inner, SagaCommitDelegate commitDelegate)
    : ISagaHandler<TSagaState, TEvent>
    where TSagaState : IState<TSagaState, TEvent> {

    /// <inheritdoc/>
    public async ValueTask HandleAsync(AggregateIdentifier sagaId, TEvent @event, CancellationToken cancellationToken = default) {
        await using var scope = new UnitOfWorkScope(new UnitOfWork(), uow => commitDelegate(uow));
        await inner.HandleAsync(sagaId, @event, cancellationToken);
        scope.Complete();
    }
}
