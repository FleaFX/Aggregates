namespace Aggregates.Policies;

/// <summary>
/// Invokes <typeparamref name="TPolicy"/>'s <see cref="IPolicy{TEvent}.ReactAsync"/> and
/// dispatches each produced command via <see cref="ICommandDispatcher"/>. The innermost link of
/// the policy handler chain.
/// </summary>
/// <typeparam name="TPolicy">The policy class.</typeparam>
/// <typeparam name="TEvent">The event type the policy reacts to.</typeparam>
sealed class PolicyHandler<TPolicy, TEvent>(TPolicy policy, ICommandDispatcher dispatcher)
    where TPolicy : IPolicy<TEvent> {

    /// <summary>
    /// Lets the policy react to <paramref name="event"/> and dispatches the produced commands.
    /// </summary>
    /// <param name="event">The event to react to.</param>
    /// <param name="cancellationToken">A cancellation token.</param>
    public async ValueTask HandleAsync(TEvent @event, CancellationToken cancellationToken = default) {
        await foreach (var command in policy.ReactAsync(@event, cancellationToken))
            await dispatcher.DispatchAsync(command, cancellationToken);
    }
}
