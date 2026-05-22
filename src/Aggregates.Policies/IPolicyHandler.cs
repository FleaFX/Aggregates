namespace Aggregates.Policies;

/// <summary>
/// Handles an incoming event by invoking the associated <see cref="IPolicy{TEvent}"/> and
/// dispatching any produced commands. Integration packages subscribe to event streams and
/// call this for each incoming event.
/// </summary>
/// <typeparam name="TEvent">The event type this handler processes.</typeparam>
public interface IPolicyHandler<in TEvent> {
    /// <summary>
    /// Handles <paramref name="event"/> by invoking the policy and dispatching the produced commands.
    /// </summary>
    ValueTask HandleAsync(TEvent @event, CancellationToken cancellationToken = default);
}
