namespace Aggregates;

/// <summary>
/// The root of an entity aggregate, maintaining state by applying a sequence of events.
/// </summary>
/// <typeparam name="TState">The type of the state object.</typeparam>
/// <typeparam name="TEvent">The type of the events applicable to this aggregate.</typeparam>
public sealed class EntityRoot<TState, TEvent>(AggregateVersion version, TState? state = default) : IAggregateRoot
    where TState : IState<TState, TEvent> {

    readonly List<TEvent> _changes = [];

    /// <summary>
    /// Gets the current state of the aggregate.
    /// </summary>
    public TState State { get; private set; } = state ?? TState.Initial;

    /// <inheritdoc/>
    public AggregateVersion Version { get; } = version;

    /// <inheritdoc/>
    public IEnumerable<object> GetChanges() => _changes.Cast<object>();

    /// <summary>
    /// Applies <paramref name="command"/> to the current state, collecting the resulting events.
    /// </summary>
    /// <param name="command">The command to apply.</param>
    /// <param name="cancellationToken">A cancellation token.</param>
    public async ValueTask AcceptAsync<TCommand>(TCommand command, CancellationToken cancellationToken = default)
        where TCommand : ICommand<TState, TEvent> {
        await foreach (var @event in command.ProgressAsync(State, cancellationToken)) {
            _changes.Add(@event);
            State = State.Apply(@event);
        }
    }
}
