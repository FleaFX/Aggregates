using Aggregates.Extensions;

namespace Aggregates;

interface IAggregateRoot {
    /// <summary>
    /// Gets the version that the aggregate instance is at.
    /// </summary>
    long Version { get; }

    /// <summary>
    /// Gets the sequence of changes that were applied, if any.
    /// </summary>
    /// <returns>A <see cref="IEnumerable{T}"/>.</returns>
    IEnumerable<object> GetChanges();
}

/// <summary>
/// Base class for domain objects that are aggregate root. 
/// </summary>
/// <typeparam name="TState">The type of the maintained state object.</typeparam>
/// <typeparam name="TEvent">The type of the event(s) that are applicable.</typeparam>
sealed record AggregateRoot<TState, TEvent>(TState? State = default, long Version = 0L) : IAggregateRoot where TState : IState<TState, TEvent> {
    readonly List<object> _changes = new();

    /// <summary>
    /// Gets the current state of the aggregate.
    /// </summary>
    TState State { get; set; } = State ?? TState.Initial;

    /// <summary>
    /// Gets the sequence of changes that were applied, if any.
    /// </summary>
    /// <returns>A <see cref="IEnumerable{T}"/>.</returns>
    public IEnumerable<object> GetChanges() => _changes;

    /// <summary>
    /// Accepts the given <paramref name="command"/> in order to progress the state of the aggregate.
    /// </summary>
    /// <param name="command">The command to accept.</param>
    public void Accept<TCommand>(TCommand command) where TCommand : ICommand<TCommand, TState, TEvent> =>
        State = command.Progress(State)
            .Tap(@event => _changes.Add(@event))
            .Aggregate(State, static (state, @event) => state.Apply(@event));
}

/// <summary>
/// Ties the <paramref name="AggregateRoot"/> to its unique <paramref name="Identifier"/> within the system.
/// </summary>
/// <param name="Identifier">Uniquely identifies the aggregate within the system.</param>
/// <param name="AggregateRoot">The root object of the aggregate.</param>
readonly record struct Aggregate(AggregateIdentifier Identifier, IAggregateRoot AggregateRoot) {
    /// <summary>
    /// Used to check whether an aggregate was loaded.
    /// </summary>
    public static Aggregate None => new();
}