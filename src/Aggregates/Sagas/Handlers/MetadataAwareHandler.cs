using Aggregates.Metadata;

namespace Aggregates.Sagas.Handlers; 

class MetadataAwareHandler<TReactionState, TReactionEvent, TCommand, TCommandState, TCommandEvent> : ISagaHandler<TReactionState, TReactionEvent, TCommand, TCommandState, TCommandEvent>
    where TReactionState : IState<TReactionState, TReactionEvent>
    where TCommand : ICommand<TCommandState, TCommandEvent>
    where TCommandState : IState<TCommandState, TCommandEvent> {

    readonly UnitOfWorkAwareHandler<TReactionState, TReactionEvent, TCommand, TCommandState, TCommandEvent> _handler;

    /// <summary>
    /// Initializes a new <see cref="MetadataAwareHandler{TReactionState,TReactionEvent,TCommand,TCommandState,TCommandEvent}"/>.
    /// </summary>
    /// <param name="handler">The handler to delegate to.</param>
    public MetadataAwareHandler(UnitOfWorkAwareHandler<TReactionState, TReactionEvent, TCommand, TCommandState, TCommandEvent> handler) =>
        _handler = handler;

    /// <summary>
    /// 
    /// </summary>
    /// <param name="event"></param>
    /// <param name="metadata"></param>
    /// <param name="cancellationToken"></param>
    /// <returns></returns>
    /// <exception cref="NotImplementedException"></exception>
    public async ValueTask HandleAsync(TReactionEvent @event, IReadOnlyDictionary<string, object?>? metadata, CancellationToken cancellationToken) {
        await using var scope = new MetadataScope();

        // copy existing metadata over to the new scope
        foreach (var pair in metadata ?? new Dictionary<string, object?>())
            scope.Add(pair);

        await _handler.HandleAsync(@event, metadata,  cancellationToken);
    }
}