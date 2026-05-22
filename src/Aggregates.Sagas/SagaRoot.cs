namespace Aggregates.Sagas;

/// <summary>
/// The root of a saga aggregate. Maintains state by applying incoming events and
/// delegates reaction logic to the saga class.
/// </summary>
/// <typeparam name="TSagaState">The type of the state object.</typeparam>
/// <typeparam name="TEvent">The type of the events applicable to this saga.</typeparam>
public sealed class SagaRoot<TSagaState, TEvent>(AggregateVersion version, TSagaState? state = default) : IAggregateRoot
    where TSagaState : IState<TSagaState, TEvent> {

    readonly List<TEvent> _changes = [];

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
    /// the commands produced by <see cref="ISaga{TSagaState,TEvent}.ReactAsync"/>.
    /// </summary>
    /// <param name="event">The event to apply.</param>
    /// <param name="saga">The saga that provides the reaction logic.</param>
    /// <param name="cancellationToken">A cancellation token.</param>
    /// <returns>
    /// All commands produced by the saga in reaction to <paramref name="event"/>.
    /// Eagerly materialized so that state is always updated regardless of whether
    /// the caller iterates the result.
    /// </returns>
    public async ValueTask<IReadOnlyList<ICommand>> AcceptAsync(TEvent @event, ISaga<TSagaState, TEvent> saga, CancellationToken cancellationToken = default) {
        _changes.Add(@event);
        State = State.Apply(@event);
        var commands = new List<ICommand>();
        await foreach (var command in saga.ReactAsync(State, @event, cancellationToken))
            commands.Add(command);
        return commands;
    }
}
