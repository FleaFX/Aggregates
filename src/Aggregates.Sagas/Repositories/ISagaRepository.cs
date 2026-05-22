namespace Aggregates.Sagas;

internal interface ISagaRepository<TSagaState, TEvent>
    where TSagaState : IState<TSagaState, TEvent> {
    /// <summary>
    /// Retrieves the <see cref="SagaRoot{TSagaState,TEvent}"/> for <paramref name="sagaId"/>.
    /// </summary>
    /// <exception cref="AggregateRootNotFoundException">Thrown when no saga exists for <paramref name="sagaId"/>.</exception>
    async ValueTask<SagaRoot<TSagaState, TEvent>> GetAsync(AggregateIdentifier sagaId, CancellationToken cancellationToken = default) {
        var root = await TryGetAsync(sagaId, cancellationToken);
        if (root is null) throw new AggregateRootNotFoundException(sagaId);
        return root;
    }

    /// <summary>
    /// Attempts to retrieve the <see cref="SagaRoot{TSagaState,TEvent}"/> for <paramref name="sagaId"/>.
    /// Returns <see langword="null"/> when not found.
    /// </summary>
    ValueTask<SagaRoot<TSagaState, TEvent>?> TryGetAsync(AggregateIdentifier sagaId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Attaches a new <paramref name="sagaRoot"/> under <paramref name="sagaId"/>.
    /// </summary>
    void Add(AggregateIdentifier sagaId, SagaRoot<TSagaState, TEvent> sagaRoot);
}
