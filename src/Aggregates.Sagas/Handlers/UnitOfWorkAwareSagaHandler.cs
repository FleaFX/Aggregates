using Aggregates.Subscriptions;

namespace Aggregates.Sagas;

/// <summary>
/// Decorates a <see cref="SagaHandler{TSaga,TSagaState,TEvent}"/> with <see cref="UnitOfWork"/>
/// lifecycle management, analogous to
/// <see cref="Aggregates.UnitOfWorkAwareCommandHandler{TCommand}"/>.
/// Creates a fresh <see cref="UnitOfWork"/> and <see cref="UnitOfWorkScope"/> for each event,
/// making the unit of work available to repositories via the ambient scope.
/// </summary>
/// <typeparam name="TSaga">The saga class.</typeparam>
/// <typeparam name="TSagaState">The saga state type.</typeparam>
/// <typeparam name="TEvent">The event type the saga reacts to.</typeparam>
sealed class UnitOfWorkAwareSagaHandler<TSaga, TSagaState, TEvent>(SagaHandler<TSaga, TSagaState, TEvent> inner, SagaCommitDelegate commitDelegate)
    where TSaga : ISaga<TSagaState, TEvent>
    where TSagaState : IState<TSagaState, TEvent> {

    /// <summary>
    /// Handles <paramref name="event"/> for <paramref name="sagaId"/> within a new unit of work,
    /// and commits it when the inner handler succeeds.
    /// </summary>
    /// <param name="sagaId">Identifies the saga instance to update.</param>
    /// <param name="event">The event to handle.</param>
    /// <param name="source">Identifies the stored event that triggered the saga.</param>
    /// <param name="cancellationToken">A cancellation token.</param>
    public async ValueTask HandleAsync(AggregateIdentifier sagaId, TEvent @event, EventReference source, CancellationToken cancellationToken = default) {
        await using var scope = new UnitOfWorkScope(new UnitOfWork(), uow => commitDelegate(uow));
        await inner.HandleAsync(sagaId, @event, source, cancellationToken);
        scope.Complete();
    }

    /// <summary>
    /// Delivers the due <paramref name="deadline"/> to <paramref name="sagaId"/> within a new unit of work,
    /// and commits it when the inner handler succeeds: only the requests of the reaction are stored.
    /// </summary>
    /// <param name="sagaId">Identifies the saga instance.</param>
    /// <param name="deadline">The payload of the deadline.</param>
    /// <param name="request">The saga stream and revision of the deadline request.</param>
    /// <param name="cancellationToken">A cancellation token.</param>
    /// <returns><see langword="false"/> when the saga doesn't exist.</returns>
    public async ValueTask<bool> HandleDeadlineAsync(AggregateIdentifier sagaId, object deadline, EventReference request, CancellationToken cancellationToken = default) {
        await using var scope = new UnitOfWorkScope(new UnitOfWork(), uow => commitDelegate(uow));
        var found = await inner.HandleDeadlineAsync(sagaId, deadline, request, cancellationToken);
        scope.Complete();
        return found;
    }
}
