using Aggregates.Subscriptions;

namespace Aggregates.Sagas;

/// <summary>
/// Remembers whether a saga ever requested a timeout, so that its timeout scheduler only runs then.
/// A saga is activated when the stream of its scheduler (or of a predecessor's) exists; the first
/// request in a process writes that stream before the request is committed.
/// </summary>
/// <param name="store">The store of the timeout entries.</param>
abstract class SagaTimeoutActivation(ISagaTimeoutStore store) {
    static readonly byte[] ActivatedMarker = "activated"u8.ToArray();

    readonly TaskCompletionSource _activated = new(TaskCreationOptions.RunContinuationsAsynchronously);
    readonly SemaphoreSlim _lock = new(1, 1);

    /// <summary>
    /// Completes when the saga is activated.
    /// </summary>
    public Task Activated => _activated.Task;

    /// <summary>
    /// Checks whether the stream of the scheduler of <paramref name="registration"/>, or of one of its
    /// predecessors, exists, and completes <see cref="Activated"/> when it does.
    /// </summary>
    /// <param name="registration">The subscription of the saga.</param>
    /// <param name="cancellationToken">A cancellation token.</param>
    public async ValueTask<bool> IsActivatedAsync(SubscriptionRegistration registration, CancellationToken cancellationToken) {
        if (_activated.Task.IsCompleted)
            return true;
        foreach (var id in (string[])[registration.SubscriptionId, .. registration.Predecessors ?? []]) {
            if (!await store.ExistsAsync(SagaTimeouts.SubscriptionId(id), cancellationToken))
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
    /// <param name="subscriptionId">The subscription id of the saga.</param>
    /// <param name="cancellationToken">A cancellation token.</param>
    protected async ValueTask ActivateAsync(string subscriptionId, CancellationToken cancellationToken) {
        await _lock.WaitAsync(cancellationToken);
        try {
            if (_activated.Task.IsCompleted)
                return;
            var stream = SagaTimeouts.SubscriptionId(subscriptionId);
            if (!await store.ExistsAsync(stream, cancellationToken))
                await store.AppendAsync(stream, SagaTimeoutFormat.MarkerEventType, ActivatedMarker, cancellationToken);
            _activated.TrySetResult();
        } finally {
            _lock.Release();
        }
    }
}

/// <summary>
/// The <see cref="SagaTimeoutActivation"/> of <typeparamref name="TSaga"/>.
/// </summary>
/// <typeparam name="TSaga">The saga class.</typeparam>
/// <param name="store">The store of the timeout entries.</param>
/// <param name="registrations">The subscriptions of the host, to find the saga's.</param>
sealed class SagaTimeoutActivation<TSaga>(ISagaTimeoutStore store, IEnumerable<SubscriptionRegistration> registrations) : SagaTimeoutActivation(store) {
    /// <summary>
    /// Activates the saga before its first timeout request is committed, so that its scheduler
    /// always finds it.
    /// </summary>
    /// <param name="cancellationToken">A cancellation token.</param>
    /// <exception cref="InvalidOperationException">The saga has no subscription.</exception>
    public ValueTask EnsureActivatedAsync(CancellationToken cancellationToken) {
        if (Activated.IsCompleted)
            return ValueTask.CompletedTask;
        var registration = registrations.FirstOrDefault(r => r.HandlerType == typeof(TSaga))
            ?? throw new InvalidOperationException(
                $"{typeof(TSaga).Name} requests a timeout, but it has no subscription: register a resolver for it, so that its timeouts are executed.");
        return ActivateAsync(registration.SubscriptionId, cancellationToken);
    }
}
