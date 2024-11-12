using Aggregates.Metadata;

namespace Aggregates.Sagas.Handlers;

class MetadataAwareHandler<TSagaState, TSagaEvent, TCommand, TCommandState, TCommandEvent>(UnitOfWorkAwareHandler<TSagaState, TSagaEvent, TCommand, TCommandState, TCommandEvent> handler) : ISagaHandler<TSagaState, TSagaEvent, TCommand, TCommandState, TCommandEvent>
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
        await using var scope = new MetadataScope();

        // copy existing metadata over to the new scope
        foreach (var pair in metadata ?? new Dictionary<string, object?>())
            scope.Add(pair);

        await handler.HandleAsync(@delegate, @event, metadata,  cancellationToken);
    }
}