using Aggregates.Extensions;
using Aggregates.Types;

namespace Aggregates.Entities;

/// <summary>
/// Base class for domain objects that are aggregate root. 
/// </summary>
/// <typeparam name="TState">The type of the maintained state object.</typeparam>
/// <typeparam name="TEvent">The type of the event(s) that are applicable.</typeparam>
sealed record EntityRoot<TState, TEvent>(TState? State, AggregateVersion Version) : IAggregateRoot where TState : IState<TState, TEvent> {
    readonly List<object> _changes = new();

    /// <summary>
    /// Gets the current state of the aggregate.
    /// </summary>
    public TState State { get; set; } = State ?? TState.Initial;

    /// <summary>
    /// Gets the sequence of changes that were applied, if any.
    /// </summary>
    /// <returns>A <see cref="IEnumerable{T}"/>.</returns>
    public IEnumerable<object> GetChanges() => _changes;

    /// <summary>
    /// Accepts the given <paramref name="command"/> in order to asynchronously progress the state of the aggregate.
    /// </summary>
    /// <param name="command">The command to accept.</param>
    /// <param name="cancellationToken">A <see cref="CancellationToken"/> to cancel the asynchronous operation.</param>
    public async ValueTask AcceptAsync<TCommand>(TCommand command, CancellationToken cancellationToken = default)
        where TCommand : ICommand<TCommand, TState, TEvent> =>
        State = await command.ProgressAsync(State, cancellationToken)
            .TapAsync(@event => _changes.Add(@event))
            .AggregateAsync(State, static (state, @event) => state.Apply(@event), cancellationToken: cancellationToken);

    public static implicit operator TState(EntityRoot<TState, TEvent> instance) => instance.State;
}