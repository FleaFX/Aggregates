namespace Aggregates.Policies;

/// <summary>
/// Abstract base for the policy handler decorator chain.
/// </summary>
abstract class PolicyHandler<TEvent> : IPolicyHandler<TEvent> {
    /// <inheritdoc/>
    public abstract ValueTask HandleAsync(TEvent @event, CancellationToken cancellationToken = default);
}

/// <summary>
/// Invokes <typeparamref name="TPolicy"/>'s <see cref="IPolicy{TEvent}.ReactAsync"/> and
/// dispatches each produced command via <see cref="ICommandDispatcher"/>.
/// </summary>
sealed class PolicyHandler<TPolicy, TEvent>(IPolicy<TEvent> policy, ICommandDispatcher dispatcher)
    : PolicyHandler<TEvent>
    where TPolicy : IPolicy<TEvent> {

    /// <inheritdoc/>
    public override async ValueTask HandleAsync(TEvent @event, CancellationToken cancellationToken = default) {
        await foreach (var command in policy.ReactAsync(@event, cancellationToken))
            await dispatcher.DispatchAsync(command, cancellationToken);
    }
}
