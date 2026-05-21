namespace Aggregates.Sagas;

/// <summary>
/// Resolves saga identifiers by delegating to a user-supplied function. The function
/// receives the incoming event and returns the target saga identifiers, or an empty
/// sequence when the event is not relevant to any saga instance.
/// </summary>
/// <remarks>
/// This is the most flexible resolver: the function can read any property of the event,
/// extract a value from an ambient context (e.g. metadata passed via a scope), or apply
/// any other logic needed to determine the saga identifiers.
/// </remarks>
/// <typeparam name="TEvent">The type of event to resolve saga identifiers for.</typeparam>
public sealed class FuncSagaIdResolver<TEvent>(Func<TEvent, IEnumerable<AggregateIdentifier>> resolve) : ISagaIdResolver<TEvent> {
    /// <inheritdoc/>
    public IEnumerable<AggregateIdentifier> Resolve(TEvent @event) => resolve(@event);
}
