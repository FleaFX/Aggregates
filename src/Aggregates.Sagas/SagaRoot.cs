using Aggregates.Subscriptions;

namespace Aggregates.Sagas;

/// <summary>
/// The root of a saga aggregate. Maintains state by applying incoming events and
/// delegates reaction logic to the saga class. Its changes are links to source events
/// (<see cref="EventReference"/>), deferred commands (<see cref="DeferredCommandRequest"/>) and
/// deadlines (<see cref="DeadlineRequest"/>).
/// </summary>
/// <typeparam name="TSagaState">The type of the state object.</typeparam>
/// <typeparam name="TEvent">The type of the events applicable to this saga.</typeparam>
/// <param name="state">The rehydrated state of the saga.</param>
/// <param name="version">The version of the saga: the number of entries in its stream.</param>
/// <param name="handled">
/// For the loaded instance: the sources the saga stream already links to, and the deadlines whose
/// reaction already stored requests in it.
/// </param>
public sealed class SagaRoot<TSagaState, TEvent>(TSagaState? state, AggregateVersion version, IReadOnlySet<EventReference>? handled = null) : IAggregateRoot
    where TSagaState : IState<TSagaState, TEvent> {

    readonly List<object> _changes = [];

    /// <summary>
    /// Gets the current state of the saga.
    /// </summary>
    public TSagaState State { get; private set; } = state ?? TSagaState.Initial;

    /// <inheritdoc/>
    public AggregateVersion Version { get; } = version;

    /// <inheritdoc/>
    public IEnumerable<object> GetChanges() => _changes;

    /// <summary>
    /// Applies <paramref name="event"/> to the current state, then eagerly materializes
    /// the commands produced by <see cref="ISaga{TSagaState,TEvent}.ReactAsync"/>, which gets the
    /// state from before <paramref name="event"/>. Records
    /// <paramref name="source"/> as a change, so the commit stores a link to the source event
    /// instead of a copy of it. A <see cref="DeferredCommand"/> or <see cref="Deadline"/> the saga
    /// yields is recorded as a change (<see cref="DeferredCommandRequest"/>, <see cref="DeadlineRequest"/>)
    /// instead of returned.
    /// </summary>
    /// <param name="event">The event to apply.</param>
    /// <param name="source">Identifies the stored event that triggered this saga.</param>
    /// <param name="saga">The saga that provides the reaction logic.</param>
    /// <param name="cancellationToken">A cancellation token.</param>
    /// <returns>
    /// All commands produced by the saga in reaction to <paramref name="event"/>, or an empty
    /// list when the saga already linked <paramref name="source"/>. Eagerly materialized so
    /// that state is always updated regardless of whether the caller iterates the result.
    /// </returns>
    /// <exception cref="InvalidOperationException">The saga requests a deadline it can't receive, or defers a deadline.</exception>
    public async ValueTask<IReadOnlyList<ICommand>> AcceptAsync(TEvent @event, EventReference source, ISaga<TSagaState, TEvent> saga, CancellationToken cancellationToken = default) {
        // A source that the saga stream already links to — before or within this unit of work —
        // must not trigger a second reaction; the commit handlers skip an empty change list.
        if (handled?.Contains(source) == true || _changes.Contains(source))
            return [];

        // The saga reacts with what it knew before the event: from that it can compute the state
        // after the event (Apply), but not the other way around.
        var before = State;
        _changes.Add(source);
        State = State.Apply(@event);
        return await CollectAsync(saga.ReactAsync(before, @event, cancellationToken), saga, cause: null);
    }

    /// <summary>
    /// Lets <paramref name="saga"/> react to a due deadline with the current state, through
    /// <see cref="ISaga{TSagaState,TEvent,TDeadline}.ReactToDeadlineAsync"/>. The state doesn't change and
    /// nothing is recorded for the deadline itself; the deferred commands and deadlines the reaction
    /// yields are recorded as changes with <paramref name="request"/> as their cause.
    /// </summary>
    /// <param name="deadline">The payload of the deadline.</param>
    /// <param name="request">The saga stream and revision of the deadline request.</param>
    /// <param name="saga">The saga that provides the reaction logic.</param>
    /// <param name="cancellationToken">A cancellation token.</param>
    /// <returns>
    /// The commands produced by the reaction, or an empty list when the saga stream already holds
    /// requests caused by <paramref name="request"/>: the deadline was delivered before.
    /// </returns>
    /// <exception cref="InvalidOperationException">
    /// The saga doesn't implement <see cref="ISaga{TSagaState,TEvent,TDeadline}"/>, <paramref name="deadline"/>
    /// is not its payload type, or the reaction requests a deadline it can't receive or defers a deadline.
    /// </exception>
    public async ValueTask<IReadOnlyList<ICommand>> AcceptDeadlineAsync(object deadline, EventReference request, ISaga<TSagaState, TEvent> saga, CancellationToken cancellationToken = default) {
        // A deadline delivered again after its reaction stored requests must not store them twice.
        if (handled?.Contains(request) == true)
            return [];

        return await CollectAsync(SagaDeadlines.ReactAsync(saga, State, deadline, cancellationToken), saga, request);
    }

    /// <summary>
    /// Runs <paramref name="reaction"/> to its end: deferred commands and deadlines become changes with
    /// <paramref name="cause"/>, the other commands are returned.
    /// </summary>
    async ValueTask<IReadOnlyList<ICommand>> CollectAsync(IAsyncEnumerable<ICommand> reaction, ISaga<TSagaState, TEvent> saga, EventReference? cause) {
        var commands = new List<ICommand>();
        await foreach (var command in reaction) {
            // Deferred commands and deadlines are stored with the saga's state and run when they are due, not now.
            switch (command) {
                case DeferredCommand { Command: Deadline }:
                    throw new InvalidOperationException(
                        $"{saga.GetType().Name} defers a deadline; request it with Deadline.At(due, payload) instead.");
                case DeferredCommand deferred:
                    _changes.Add(new DeferredCommandRequest(SagaOwners.Of(saga.GetType()), deferred.Due, deferred.Command) { Cause = cause });
                    break;
                case Deadline deadline:
                    _changes.Add(new DeadlineRequest(SagaOwners.Of(saga.GetType()), deadline.Due, SagaDeadlines.Check(saga.GetType(), deadline.Payload)) { Cause = cause });
                    break;
                default:
                    commands.Add(command);
                    break;
            }
        }
        return commands;
    }
}
