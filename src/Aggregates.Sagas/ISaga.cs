namespace Aggregates.Sagas;

/// <summary>
/// Implements the reaction logic for a saga. The implementing class may declare
/// constructor parameters — the DI container resolves them at runtime.
/// </summary>
/// <remarks>
/// A class implements this interface once, and gets one subscription with its own checkpoint.
/// Registration fails when a class implements it for several event types: the saga state is
/// rebuilt from the events of <typeparamref name="TEvent"/> only. To receive deadlines as well,
/// implement <see cref="ISaga{TSagaState,TEvent,TDeadline}"/> instead.
/// </remarks>
/// <typeparam name="TSagaState">The type of the state object maintained by the saga.</typeparam>
/// <typeparam name="TEvent">
/// The type of events this saga reacts to. Use a marker interface to react to multiple event types,
/// and pattern matching in <see cref="Correlate"/>, <see cref="IState{TState,TEvent}.Apply"/> and <see cref="ReactAsync"/>.
/// </typeparam>
public interface ISaga<in TSagaState, in TEvent>
    where TSagaState : IState<TSagaState, TEvent> {
    /// <summary>
    /// Returns the identifier of each saga instance that reacts to <paramref name="event"/>: the names of
    /// their saga streams. Returns an empty sequence when no instance reacts to it.
    /// </summary>
    /// <remarks>
    /// Called once per event, on an instance resolved from a fresh DI scope, before any saga instance
    /// handles it. A failure is retried, and the event parked like a failing reaction. Derive the
    /// identifiers from the event and its metadata only, so that a redelivered or replayed event reaches
    /// the same instances. The identifiers belong to this saga: two sagas that return the same identifier
    /// share a saga stream, and an aggregate's stream is not a saga stream.
    /// </remarks>
    /// <param name="event">The incoming event.</param>
    /// <param name="metadata">
    /// The metadata stored with <paramref name="event"/>, for identifiers that are carried in metadata
    /// rather than in the event itself.
    /// </param>
    IEnumerable<AggregateIdentifier> Correlate(TEvent @event, EventMetadata metadata);

    /// <summary>
    /// Reacts to <paramref name="event"/> by producing zero or more commands to execute.
    /// The caller is responsible for dispatching the produced commands. Besides commands, the saga can
    /// yield commands deferred with <see cref="DeferredCommandExtensions.DeferUntil"/>, and, when it
    /// implements <see cref="ISaga{TSagaState,TEvent,TDeadline}"/>, deadlines requested with
    /// <see cref="Deadline.At"/>.
    /// </summary>
    /// <param name="state">
    /// The saga state before <paramref name="event"/>: what the saga knew until now. The event is
    /// applied afterwards; use <c>state.Apply(@event)</c> for the state after it.
    /// </param>
    /// <param name="event">The triggering event.</param>
    /// <param name="cancellationToken">A cancellation token.</param>
    IAsyncEnumerable<ICommand> ReactAsync(TSagaState state, TEvent @event, CancellationToken cancellationToken = default);
}

/// <summary>
/// A saga that also receives the deadlines it requests with <see cref="Deadline.At"/>. It is a
/// <see cref="ISaga{TSagaState,TEvent}"/> in every other respect: implement this interface instead of
/// that one, once.
/// </summary>
/// <remarks>
/// A deadline doesn't change the saga's state: <see cref="IState{TState,TEvent}.Apply"/> is not called,
/// and nothing is stored for the deadline itself. The saga decides from its current state and the
/// payload, and learns what happened from the events of the commands it produces. Delivery is at least
/// once and can be late, and the state can lag behind the aggregates, so the saga recognises a
/// deadline that no longer applies from its payload and its state, and ignores it.
/// </remarks>
/// <typeparam name="TSagaState">The type of the state object maintained by the saga.</typeparam>
/// <typeparam name="TEvent">The type of events this saga reacts to.</typeparam>
/// <typeparam name="TDeadline">
/// The type of the deadline payloads this saga receives. Use a marker interface (or object) to receive
/// several kinds of deadlines, and pattern matching in <see cref="ReactToDeadlineAsync"/>.
/// </typeparam>
public interface ISaga<in TSagaState, in TEvent, in TDeadline> : ISaga<TSagaState, TEvent>
    where TSagaState : IState<TSagaState, TEvent> {
    /// <summary>
    /// Reacts to a deadline that is due by producing zero or more commands to execute: commands,
    /// commands deferred with <see cref="DeferredCommandExtensions.DeferUntil"/>, and new deadlines.
    /// </summary>
    /// <param name="state">The current state of the saga.</param>
    /// <param name="deadline">The payload the saga requested the deadline with.</param>
    /// <param name="cancellationToken">A cancellation token.</param>
    IAsyncEnumerable<ICommand> ReactToDeadlineAsync(TSagaState state, TDeadline deadline, CancellationToken cancellationToken = default);
}
