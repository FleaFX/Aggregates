namespace Aggregates.Sagas;

/// <summary>
/// The root of a saga aggregate.
/// </summary>
/// <typeparam name="TState">The type of the maintained state object.</typeparam>
/// <typeparam name="TEvent">The type of the event(s) that are applicable.</typeparam>
/// <param name="State">The state of the aggregate.</param>
/// <param name="Version">The version of the aggregate when it was loaded.</param>
sealed record SagaRoot<TState, TEvent>(TState State, AggregateVersion Version) : IAggregateRoot
    where TState : IState<TState, TEvent> {
    readonly List<object> _changes = new();

    /// <summary>
    /// Accepts the given <paramref name="event"/> and produces a sequence of commands to execute in reaction.
    /// </summary>
    /// <typeparam name="TCommand">The type of the commands to execute.</typeparam>
    /// <typeparam name="TCommandState">The type of the state object that is acted upon by the produced commands.</typeparam>
    /// <typeparam name="TCommandEvent">The type of the event(s) that are applicable to the state that is acted upon by the produced commands.</typeparam>
    /// <param name="reaction">The <see cref="IReaction{TState, TReactionEvent,TCommand,TState,TEvent}"/> that implements the reaction logic.</param>
    /// <param name="event">The event to react to.</param>
    /// <param name="metadata">The metadata that is associated with the event.</param>
    /// <param name="cancellationToken">A <see cref="CancellationToken"/> to cancel the asynchronous operation.</param>
    /// <returns></returns>
    public IAsyncEnumerable<TCommand> AcceptAsync<TCommand, TCommandState, TCommandEvent>(IReaction<TState, TEvent, TCommand, TCommandState, TCommandEvent> reaction, TEvent @event, IReadOnlyDictionary<string, object?> metadata, CancellationToken cancellationToken = default)
        where TCommand : ICommand<TCommandState, TCommandEvent>
        where TCommandState : IState<TCommandState, TCommandEvent> {
        _changes.Add(@event);
        return reaction.ReactAsync(State, @event, metadata, cancellationToken);
    }

    /// <summary>
    /// Gets the sequence of changes that were applied, if any.
    /// </summary>
    /// <returns>A <see cref="IEnumerable{T}"/>.</returns>
    public IEnumerable<object> GetChanges() => _changes;
}