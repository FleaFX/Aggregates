namespace Aggregates.Sagas;

/// <summary>
/// Resolves saga identifiers by delegating to a user-supplied function.
/// </summary>
/// <remarks>
/// Two factory signatures are supported:
/// <list type="bullet">
///   <item>
///     <c>Func&lt;TEvent, IEnumerable&lt;AggregateIdentifier&gt;&gt;</c> — for resolvers that
///     derive the saga identifier purely from the event body.
///   </item>
///   <item>
///     <c>Func&lt;TEvent, EventMetadata, IEnumerable&lt;AggregateIdentifier&gt;&gt;</c> — for
///     resolvers that need the stored event metadata (e.g. when saga identifiers are written
///     to metadata rather than the event body).
///   </item>
/// </list>
/// </remarks>
/// <typeparam name="TEvent">The type of event to resolve saga identifiers for.</typeparam>
public sealed class FuncSagaIdResolver<TEvent>(Func<TEvent, EventMetadata, IEnumerable<AggregateIdentifier>> resolve) : ISagaIdResolver<TEvent> {
    /// <summary>
    /// Initializes a new <see cref="FuncSagaIdResolver{TEvent}"/> that derives saga identifiers
    /// from the event body only, ignoring metadata.
    /// </summary>
    public FuncSagaIdResolver(Func<TEvent, IEnumerable<AggregateIdentifier>> resolve)
        : this((evt, _) => resolve(evt)) { }

    /// <inheritdoc/>
    public IEnumerable<AggregateIdentifier> Resolve(TEvent @event, EventMetadata metadata) =>
        resolve(@event, metadata);
}
