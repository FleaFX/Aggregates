using Aggregates.Subscriptions;

namespace Aggregates.Sagas;

/// <summary>
/// The root of a saga aggregate. Maintains state by applying incoming events and
/// delegates reaction logic to the saga class.
/// </summary>
/// <typeparam name="TSagaState">The type of the state object.</typeparam>
/// <typeparam name="TEvent">The type of the events applicable to this saga.</typeparam>
/// <param name="state">The rehydrated state of the saga.</param>
/// <param name="version">The version of the saga: the number of entries in its stream.</param>
/// <param name="handled">The sources the saga stream already links to, for the loaded instance.</param>
public sealed class SagaRoot<TSagaState, TEvent>(TSagaState? state, AggregateVersion version, IReadOnlySet<EventReference>? handled = null) : IAggregateRoot
    where TSagaState : IState<TSagaState, TEvent> {

    readonly List<EventReference> _changes = [];

    /// <summary>
    /// Gets the current state of the saga.
    /// </summary>
    public TSagaState State { get; private set; } = state ?? TSagaState.Initial;

    /// <inheritdoc/>
    public AggregateVersion Version { get; } = version;

    /// <inheritdoc/>
    public IEnumerable<object> GetChanges() => _changes.Cast<object>();

    /// <summary>
    /// Applies <paramref name="event"/> to the current state, then eagerly materializes
    /// the commands produced by <see cref="ISaga{TSagaState,TEvent}.ReactAsync"/>. Records
    /// <paramref name="source"/> as a change, so the commit stores a link to the source event
    /// instead of a copy of it.
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
    public async ValueTask<IReadOnlyList<ICommand>> AcceptAsync(TEvent @event, EventReference source, ISaga<TSagaState, TEvent> saga, CancellationToken cancellationToken = default) {
        // A source that the saga stream already links to — before or within this unit of work —
        // must not trigger a second reaction; the commit handlers skip an empty change list.
        if (handled?.Contains(source) == true || _changes.Contains(source))
            return [];

        _changes.Add(source);
        State = State.Apply(@event);
        var commands = new List<ICommand>();
        await foreach (var command in saga.ReactAsync(State, @event, cancellationToken))
            commands.Add(command);
        return commands;
    }
}
