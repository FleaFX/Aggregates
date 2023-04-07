namespace Aggregates; 

/// <summary>
/// Thrown when retrieving an <see cref="AggregateRoot{TState,TEvent}"/> for a non-existent <see cref="AggregateIdentifier"/>.
/// </summary>
public class AggregateRootNotFoundException : Exception {
    /// <summary>
    /// Gets the <see cref="AggregateIdentifier"/> that was used to retrieve the aggregate root object.
    /// </summary>
    public AggregateIdentifier Identifier { get; }

    /// <summary>
    /// Initializes a new <see cref="AggregateRootNotFoundException"/>.
    /// </summary>
    /// <param name="identifier">The <see cref="AggregateIdentifier"/> that was used to retrieve the aggregate root object.</param>
    public AggregateRootNotFoundException(AggregateIdentifier identifier) => Identifier = identifier;
}