namespace Aggregates.Sagas.Handlers;

/// <summary>
/// Handler that commits changes tracked by the given <see cref="UnitOfWork"/>.
/// </summary>
class UnitOfWorkAwareHandler<TSagaState, TSagaEvent, TCommand, TCommandState, TCommandEvent> (
    UnitOfWork unitOfWork,
    SagaCommitDelegate commitDelegate,
    DefaultHandler<TSagaState, TSagaEvent, TCommand, TCommandState, TCommandEvent> handler
) : ISagaHandler<TSagaState, TSagaEvent, TCommand, TCommandState, TCommandEvent>
    where TSagaState : IState<TSagaState, TSagaEvent> 
    where TCommand : ICommand<TCommandState, TCommandEvent>
    where TCommandState : IState<TCommandState, TCommandEvent> {

    /// <summary>
    /// Asynchronously handles the given <paramref name="event"/>.
    /// </summary>
    /// <param name="event">The event to handle.</param>
    /// <param name="metadata">The metadata associated with the event.</param>
    /// <param name="cancellationToken">A <see cref="CancellationToken"/> to cancel the asynchronous operation.</param>
    /// <returns>A <see cref="ValueTask"/> that represents the asynchronous operation.</returns>
    public async ValueTask HandleAsync(SagaAsyncDelegate<TSagaState, TSagaEvent, TCommand, TCommandState, TCommandEvent> @delegate, TSagaEvent @event, IReadOnlyDictionary<string, object?>? metadata, CancellationToken cancellationToken) {
        await using var scope = new UnitOfWorkScope(unitOfWork, commitDelegate);
        await handler.HandleAsync(@delegate, @event, metadata, cancellationToken);
        scope.Complete();
    }
}