namespace Aggregates.Sagas;

/// <summary>
/// Decorates a <see cref="UnitOfWorkAwareSagaHandler{TSaga,TSagaState,TEvent}"/> with automatic
/// retry on <see cref="ConcurrencyException"/>. Each retry re-executes the full handler, so the
/// inner handler creates a fresh <see cref="UnitOfWork"/> and <see cref="UnitOfWorkScope"/> on
/// every attempt.
/// </summary>
/// <typeparam name="TSaga">The saga class.</typeparam>
/// <typeparam name="TSagaState">The saga state type.</typeparam>
/// <typeparam name="TEvent">The event type the saga reacts to.</typeparam>
sealed class RetrySagaHandler<TSaga, TSagaState, TEvent>(UnitOfWorkAwareSagaHandler<TSaga, TSagaState, TEvent> inner, int maxAttempts = 3)
    where TSaga : ISaga<TSagaState, TEvent>
    where TSagaState : IState<TSagaState, TEvent> {

    /// <summary>
    /// Handles <paramref name="event"/> for <paramref name="sagaId"/>, retrying on
    /// <see cref="ConcurrencyException"/> up to the maximum number of attempts.
    /// </summary>
    /// <param name="sagaId">Identifies the saga instance to update.</param>
    /// <param name="event">The event to handle.</param>
    /// <param name="cancellationToken">A cancellation token.</param>
    public async ValueTask HandleAsync(AggregateIdentifier sagaId, TEvent @event, CancellationToken cancellationToken = default) {
        var attempt = 0;
        while (true) {
            try {
                await inner.HandleAsync(sagaId, @event, cancellationToken);
                return;
            } catch (ConcurrencyException) when (++attempt < maxAttempts) {
                // The inner handler's UnitOfWorkScope.DisposeAsync already cleared the
                // UnitOfWork (via the try/finally in CommitAndClearAsync), so the next
                // attempt starts with a clean slate.
            }
        }
    }
}
