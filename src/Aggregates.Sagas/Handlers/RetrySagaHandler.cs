namespace Aggregates.Sagas;

/// <summary>
/// Decorates a <see cref="UnitOfWorkAwareSagaHandler{TSagaState,TEvent}"/> with automatic retry on
/// <see cref="ConcurrencyException"/>. Each retry re-executes the full handler, so the inner
/// handler creates a fresh <see cref="UnitOfWork"/> and <see cref="UnitOfWorkScope"/> on every
/// attempt.
/// </summary>
/// <typeparam name="TSagaState">The saga state type.</typeparam>
/// <typeparam name="TEvent">The event type this handler processes.</typeparam>
sealed class RetrySagaHandler<TSagaState, TEvent>(UnitOfWorkAwareSagaHandler<TSagaState, TEvent> inner, int maxAttempts = 3)
    : ISagaHandler<TSagaState, TEvent>
    where TSagaState : IState<TSagaState, TEvent> {

    /// <inheritdoc/>
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
