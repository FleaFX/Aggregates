namespace Aggregates.Policies;

/// <summary>
/// Implements the reaction logic for a policy. Unlike a saga, a policy has no state —
/// it reacts to each event in isolation and produces zero or more commands.
/// The implementing class may declare constructor parameters; the DI container resolves them.
/// </summary>
/// <typeparam name="TEvent">The type of event this policy reacts to.</typeparam>
public interface IPolicy<in TEvent> {
    /// <summary>
    /// Reacts to <paramref name="event"/> by producing zero or more commands to dispatch.
    /// </summary>
    IAsyncEnumerable<ICommand> ReactAsync(TEvent @event, CancellationToken cancellationToken = default);
}
