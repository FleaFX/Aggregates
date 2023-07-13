namespace Aggregates;

/// <summary>
/// Marker interface for saga handlers.
/// </summary>
/// <typeparam name="TReactionEvent">The type of the event(s) to react to.</typeparam>
#pragma warning disable CS1712
interface ISagaHandler<TReactionState, in TReactionEvent, TCommand, TCommandState, TCommandEvent>
    where TReactionState : IState<TReactionState, TReactionEvent>
    where TCommand : ICommand<TCommandState, TCommandEvent>
    where TCommandState : IState<TCommandState, TCommandEvent> {
#pragma warning restore CS1712
    /// <summary>
    /// Asynchronously handles the given <paramref name="event"/>.
    /// </summary>
    /// <param name="event">The event to handle.</param>
    /// <param name="metadata">The metadata associated with the event.</param>
    /// <param name="cancellationToken">A <see cref="CancellationToken"/> to cancel the asynchronous operation.</param>
    /// <returns>A <see cref="ValueTask"/> that represents the asynchronous operation.</returns>
    ValueTask HandleAsync(TReactionEvent @event, IReadOnlyDictionary<string, object?>? metadata, CancellationToken cancellationToken);
}