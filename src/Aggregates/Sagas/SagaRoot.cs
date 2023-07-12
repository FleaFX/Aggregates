using Aggregates.Types;

namespace Aggregates.Sagas; 

sealed record SagaRoot<TState, TEvent>(TState? State, AggregateVersion Version) : IAggregateRoot
    where TState : IState<TState, TEvent> {
    readonly List<object> _changes = new();

    /// <summary>
    /// Gets the sequence of changes that were applied, if any.
    /// </summary>
    /// <returns>A <see cref="IEnumerable{T}"/>.</returns>
    public IEnumerable<object> GetChanges() => _changes;
}