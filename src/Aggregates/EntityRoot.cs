using System.Collections.Concurrent;
using System.Reflection;

namespace Aggregates;

/// <summary>
/// The root of an entity aggregate, maintaining state by applying a sequence of events.
/// </summary>
/// <typeparam name="TState">The type of the state object.</typeparam>
/// <typeparam name="TEvent">The type of the events applicable to this aggregate.</typeparam>
public sealed class EntityRoot<TState, TEvent>(TState? state, AggregateVersion version) : IAggregateRoot
    where TState : IState<TState, TEvent> {

    // Cache MetadataAttribute lookups per type to avoid repeated reflection on the hot path.
    static readonly ConcurrentDictionary<Type, MetadataAttribute[]> _attributeCache = new();

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
    /// For each event produced, metadata is collected from the updated state and the command
    /// (in that order) into the ambient <see cref="MetadataScope"/>, if active.
    /// </summary>
    /// <param name="command">The command to apply.</param>
    /// <param name="cancellationToken">A cancellation token.</param>
    public async ValueTask AcceptAsync<TCommand>(TCommand command, CancellationToken cancellationToken = default)
        where TCommand : ICommand<TState, TEvent> {
        await foreach (var @event in command.ProgressAsync(State, cancellationToken)) {
            _changes.Add(@event);
            State = State.Apply(@event);
            await CollectMetadataAsync(State, cancellationToken);
            await CollectMetadataAsync(command, cancellationToken);
        }
    }

    static async ValueTask CollectMetadataAsync(object? context, CancellationToken cancellationToken) {
        if (context is null) return;
        var scope = MetadataScope.Current;
        if (scope is null) return;

        var attributes = _attributeCache.GetOrAdd(
            context.GetType(),
            static t => t.GetCustomAttributes<MetadataAttribute>(inherit: true).ToArray());

        foreach (var attr in attributes) {
            var value = await attr.GetValueAsync(context, cancellationToken);
            scope.Add(attr.Key, value, attr.Multiplicity);
        }
    }
}
