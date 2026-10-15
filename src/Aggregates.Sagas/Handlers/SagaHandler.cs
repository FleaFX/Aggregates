using Aggregates.Subscriptions;

namespace Aggregates.Sagas;

/// <summary>
/// Default saga handler and innermost link of the saga handler chain. Loads or creates the
/// <see cref="SagaRoot{TSagaState,TEvent}"/>, calls <see cref="SagaRoot{TSagaState,TEvent}.AcceptAsync"/>
/// with <typeparamref name="TSaga"/> to collect the produced commands, then dispatches each command
/// via <see cref="ICommandDispatcher"/>. It activates the saga's scheduler when the saga defers its
/// first command or requests its first deadline.
/// </summary>
/// <typeparam name="TSaga">The saga class.</typeparam>
/// <typeparam name="TSagaState">The saga state type.</typeparam>
/// <typeparam name="TEvent">The event type the saga reacts to.</typeparam>
sealed class SagaHandler<TSaga, TSagaState, TEvent>(ISagaRepository<TSagaState, TEvent> repository, TSaga saga, ICommandDispatcher dispatcher, SchedulerActivation<TSaga> activation)
    where TSaga : ISaga<TSagaState, TEvent>
    where TSagaState : IState<TSagaState, TEvent> {

    /// <summary>
    /// Handles <paramref name="event"/> for the saga identified by <paramref name="sagaId"/>.
    /// </summary>
    /// <param name="sagaId">Identifies the saga instance to update.</param>
    /// <param name="event">The event to handle.</param>
    /// <param name="source">Identifies the stored event that triggered the saga.</param>
    /// <param name="cancellationToken">A cancellation token.</param>
    public async ValueTask HandleAsync(AggregateIdentifier sagaId, TEvent @event, EventReference source, CancellationToken cancellationToken = default) {
        var sagaRoot = await repository.TryGetAsync(sagaId, cancellationToken);
        if (sagaRoot is null) {
            sagaRoot = new SagaRoot<TSagaState, TEvent>(default, AggregateVersion.None);
            repository.Add(sagaId, sagaRoot);
        }
        var commands = await sagaRoot.AcceptAsync(@event, source, saga, cancellationToken);
        // A saga's first deferred command or deadline activates its scheduler before the request is committed
        // (after this handler returns), so the scheduler never misses it.
        if (sagaRoot.GetChanges().Any(change => change is DeferredCommandRequest or DeadlineRequest))
            await activation.EnsureActivatedAsync(cancellationToken);
        foreach (var command in commands)
            await dispatcher.DispatchAsync(command, cancellationToken);
    }

    /// <summary>
    /// Delivers the due <paramref name="deadline"/> to the saga identified by <paramref name="sagaId"/>: the
    /// saga reacts with its current state, which doesn't change, and the commands it produces are
    /// dispatched. Activates the saga's scheduler when the reaction makes a request.
    /// </summary>
    /// <param name="sagaId">Identifies the saga instance: the stream of the deadline request.</param>
    /// <param name="deadline">The payload of the deadline.</param>
    /// <param name="request">The saga stream and revision of the deadline request.</param>
    /// <param name="cancellationToken">A cancellation token.</param>
    /// <returns><see langword="false"/> when the saga doesn't exist (its stream was deleted): nothing happens.</returns>
    public async ValueTask<bool> HandleDeadlineAsync(AggregateIdentifier sagaId, object deadline, EventReference request, CancellationToken cancellationToken = default) {
        // The request is stored in the saga stream, so only an existing saga receives a deadline.
        if (await repository.TryGetAsync(sagaId, cancellationToken) is not { } sagaRoot)
            return false;
        var commands = await sagaRoot.AcceptDeadlineAsync(deadline, request, saga, cancellationToken);
        if (sagaRoot.GetChanges().Any(change => change is DeferredCommandRequest or DeadlineRequest))
            await activation.EnsureActivatedAsync(cancellationToken);
        foreach (var command in commands)
            await dispatcher.DispatchAsync(command, cancellationToken);
        return true;
    }
}
