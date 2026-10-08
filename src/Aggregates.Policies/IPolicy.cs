namespace Aggregates.Policies;

/// <summary>
/// Implements the reaction logic for a policy. Unlike a saga, a policy has no state —
/// it reacts to each event in isolation and produces zero or more commands.
/// The implementing class may declare constructor parameters; the DI container resolves them.
/// </summary>
/// <remarks>
/// A class implements this interface once, and gets one subscription with its own checkpoint.
/// Registration fails when a class implements it for several event types.
/// </remarks>
/// <typeparam name="TEvent">
/// The type of event this policy reacts to. Use a marker interface (or <see cref="object"/>) to
/// react to multiple event types, in the order they were stored.
/// </typeparam>
public interface IPolicy<in TEvent> {
    /// <summary>
    /// Reacts to <paramref name="event"/> by producing zero or more commands to dispatch.
    /// </summary>
    IAsyncEnumerable<ICommand> ReactAsync(TEvent @event, CancellationToken cancellationToken = default);
}
