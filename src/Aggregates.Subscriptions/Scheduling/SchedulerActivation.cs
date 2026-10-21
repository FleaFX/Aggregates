using System.ComponentModel;

namespace Aggregates.Subscriptions.Internal;

/// <summary>
/// Remembers whether a saga or policy ever deferred a command, so that its scheduler only runs then.
/// A handler is activated when the stream of its scheduler (or of a predecessor's) exists; the first
/// request in a process writes that stream before the request is stored.
/// </summary>
/// <remarks>
/// Internal infrastructure of the Aggregates packages. Public only so that those packages can use it;
/// it is not covered by semantic versioning and may change in any release.
/// </remarks>
[EditorBrowsable(EditorBrowsableState.Never)]
public abstract class SchedulerActivation {
    static readonly byte[] ActivatedMarker = "activated"u8.ToArray();

    readonly IScheduledStore _store;
    readonly TaskCompletionSource _activated = new(TaskCreationOptions.RunContinuationsAsynchronously);
    readonly SemaphoreSlim _lock = new(1, 1);

    /// <summary>
    /// Initializes a new <see cref="SchedulerActivation"/>.
    /// </summary>
    /// <param name="store">The store of the scheduled entries.</param>
    private protected SchedulerActivation(IScheduledStore store) => _store = store;

    /// <summary>
    /// Completes when the handler is activated.
    /// </summary>
    public Task Activated => _activated.Task;

    /// <summary>
    /// Checks whether the stream of the scheduler of <paramref name="registration"/>, or of one of its
    /// predecessors, exists, and completes <see cref="Activated"/> when it does.
    /// </summary>
    /// <param name="registration">The subscription of the handler.</param>
    /// <param name="cancellationToken">A cancellation token.</param>
    internal async ValueTask<bool> IsActivatedAsync(SubscriptionRegistration registration, CancellationToken cancellationToken) {
        if (_activated.Task.IsCompleted)
            return true;
        foreach (var id in (string[])[registration.SubscriptionId, .. registration.Predecessors ?? []]) {
            if (!await _store.ExistsAsync(ScheduledFormat.SchedulerStream(id), cancellationToken))
                continue;
            _activated.TrySetResult();
            return true;
        }
        return false;
    }

    /// <summary>
    /// Writes the stream of the scheduler of <paramref name="subscriptionId"/> unless it exists, and
    /// completes <see cref="Activated"/>.
    /// </summary>
    /// <param name="subscriptionId">The subscription id of the handler.</param>
    /// <param name="cancellationToken">A cancellation token.</param>
    private protected async ValueTask ActivateAsync(string subscriptionId, CancellationToken cancellationToken) {
        await _lock.WaitAsync(cancellationToken);
        try {
            if (_activated.Task.IsCompleted)
                return;
            var stream = ScheduledFormat.SchedulerStream(subscriptionId);
            if (!await _store.ExistsAsync(stream, cancellationToken))
                await _store.AppendAsync(stream, ScheduledFormat.MarkerEventType, ActivatedMarker, cancellationToken);
            _activated.TrySetResult();
        } finally {
            _lock.Release();
        }
    }
}

/// <summary>
/// The <see cref="SchedulerActivation"/> of the saga or policy <typeparamref name="THandler"/>.
/// Registered as a singleton per handler class.
/// </summary>
/// <remarks>
/// Internal infrastructure of the Aggregates packages. Public only so that those packages can use it;
/// it is not covered by semantic versioning and may change in any release.
/// </remarks>
/// <typeparam name="THandler">The saga or policy class.</typeparam>
/// <param name="store">The store of the scheduled entries.</param>
/// <param name="registrations">The subscriptions of the host, to find the handler's.</param>
[EditorBrowsable(EditorBrowsableState.Never)]
public sealed class SchedulerActivation<THandler>(IScheduledStore store, IEnumerable<SubscriptionRegistration> registrations) : SchedulerActivation(store) {
    readonly Lazy<SubscriptionRegistration?> _registration = new(() => registrations.FirstOrDefault(r => r.HandlerType == typeof(THandler)));

    /// <summary>
    /// The subscription id of <typeparamref name="THandler"/>: the owner of the commands it defers.
    /// </summary>
    /// <exception cref="InvalidOperationException">The handler has no subscription.</exception>
    public string SubscriptionId => Registration.SubscriptionId;

    /// <summary>
    /// Activates the handler before its first request is stored, so that its scheduler always finds it.
    /// </summary>
    /// <param name="cancellationToken">A cancellation token.</param>
    /// <exception cref="InvalidOperationException">The handler has no subscription.</exception>
    public ValueTask EnsureActivatedAsync(CancellationToken cancellationToken) =>
        Activated.IsCompleted ? ValueTask.CompletedTask : ActivateAsync(Registration.SubscriptionId, cancellationToken);

    SubscriptionRegistration Registration => _registration.Value
        ?? throw new InvalidOperationException(
            $"{typeof(THandler).Name} defers a command, but it has no subscription, so its deferred commands would never run. Register it with AddSagas or AddPolicies.");
}
