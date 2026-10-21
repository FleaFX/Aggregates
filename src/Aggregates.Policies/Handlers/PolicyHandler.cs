using Aggregates.Subscriptions;
using Aggregates.Subscriptions.Internal;
using Microsoft.Extensions.Logging;

namespace Aggregates.Policies;

/// <summary>
/// Invokes <typeparamref name="TPolicy"/>'s <see cref="IPolicy{TEvent}.ReactAsync"/> and
/// dispatches each produced command via <see cref="ICommandDispatcher"/>. The innermost link of
/// the policy handler chain.
/// </summary>
/// <remarks>
/// A <see cref="DeferredCommand"/> the policy yields is not dispatched: once the reaction is done
/// and every other command was dispatched, the deferred commands are stored in one append to a
/// stream of their own for the event (<see cref="ScheduledFormat.RequestStream"/>), which must not
/// exist yet. A reaction that runs again for the same event (a retry, or a restart before the
/// checkpoint) finds that stream and stores nothing. The policy's scheduler executes them when due.
/// </remarks>
/// <typeparam name="TPolicy">The policy class.</typeparam>
/// <typeparam name="TEvent">The event type the policy reacts to.</typeparam>
sealed partial class PolicyHandler<TPolicy, TEvent>(
    TPolicy policy,
    ICommandDispatcher dispatcher,
    SchedulerActivation<TPolicy> activation,
    IScheduledStore store,
    ILogger<PolicyHandler<TPolicy, TEvent>> logger
)
    where TPolicy : IPolicy<TEvent> {

    /// <summary>
    /// Lets the policy react to <paramref name="event"/>, dispatches the produced commands, and then
    /// stores the deferred ones.
    /// </summary>
    /// <param name="event">The event to react to.</param>
    /// <param name="source">Identifies the stored event the policy reacts to.</param>
    /// <param name="cancellationToken">A cancellation token.</param>
    public async ValueTask HandleAsync(TEvent @event, EventReference source, CancellationToken cancellationToken = default) {
        List<DeferredCommand>? deferred = null;
        await foreach (var command in policy.ReactAsync(@event, cancellationToken)) {
            if (command is DeferredCommand deferredCommand)
                (deferred ??= []).Add(deferredCommand);
            else
                await dispatcher.DispatchAsync(command, cancellationToken);
        }
        if (deferred is null)
            return;

        // Activated before the requests are stored, so the scheduler never misses them.
        await activation.EnsureActivatedAsync(cancellationToken);
        var owner = activation.SubscriptionId;
        DeferredCommandRequest[] requests = [.. deferred.Select(command => new DeferredCommandRequest(owner, command.Due, command.Command))];
        if (!await store.TryAppendRequestsAsync(ScheduledFormat.RequestStream(owner, source), requests, cancellationToken))
            LogAlreadyStored(logger, typeof(TPolicy).Name, source);
    }

    [LoggerMessage(Level = LogLevel.Debug, Message = "Policy {Policy} already stored the deferred commands of {Source}")]
    static partial void LogAlreadyStored(ILogger logger, string policy, EventReference source);
}
