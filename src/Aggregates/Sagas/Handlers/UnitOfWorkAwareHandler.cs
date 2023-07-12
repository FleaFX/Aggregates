using Aggregates.Types;

namespace Aggregates.Sagas.Handlers;

/// <summary>
/// Handler that commits changes tracked by the given <see cref="UnitOfWork"/>.
/// </summary>
class UnitOfWorkAwareHandler<TReactionState, TReactionEvent, TCommand, TCommandState, TCommandEvent>
    where TReactionState : IState<TReactionState, TReactionEvent>
    where TCommand : ICommand<TCommand, TCommandState, TCommandEvent>
    where TCommandState : IState<TCommandState, TCommandEvent> {
    readonly UnitOfWork _unitOfWork;
    readonly CommitDelegate _commitDelegate;
    readonly DefaultHandler<TReactionState, TReactionEvent, TCommand, TCommandState, TCommandEvent> _handler;

    /// <summary>
    /// Initializes a new <see cref="UnitOfWorkAwareHandler{TReactionState,TReactionEvent,TCommand,TCommandState,TCommandEvent}"/>.
    /// </summary>
    /// <param name="unitOfWork">The <see cref="UnitOfWork"/> that tracks changes.</param>
    /// <param name="commitDelegate">The <see cref="CommitDelegate"/> that commits the changes made.</param>
    /// <param name="handler">THe handler that performs the actual work.</param>
    public UnitOfWorkAwareHandler(
        UnitOfWork unitOfWork,
        CommitDelegate commitDelegate,
        DefaultHandler<TReactionState, TReactionEvent, TCommand, TCommandState, TCommandEvent> handler) {
        _unitOfWork = unitOfWork;
        _commitDelegate = commitDelegate;
        _handler = handler;
    }

    /// <summary>
    /// Asynchronously handles the given <paramref name="event"/>.
    /// </summary>
    /// <param name="event">The event to handle.</param>
    /// <param name="metadata">The metadata associated with the event.</param>
    /// <param name="cancellationToken">A <see cref="CancellationToken"/> to cancel the asynchronous operation.</param>
    /// <returns>A <see cref="ValueTask"/> that represents the asynchronous operation.</returns>
    public async ValueTask HandleAsync(TReactionEvent @event, IReadOnlyDictionary<string, object?>? metadata, CancellationToken cancellationToken) {
        await using var scope = new UnitOfWorkScope(_unitOfWork, _commitDelegate);
        await _handler.HandleAsync(@event, metadata, cancellationToken);
        scope.Complete();
    }
}