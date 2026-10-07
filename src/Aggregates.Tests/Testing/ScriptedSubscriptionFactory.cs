using Aggregates.Subscriptions;

namespace Aggregates.Testing;

/// <summary>
/// An <see cref="ISubscriptionFactory"/> that plays one script per call to <see cref="Subscribe"/>,
/// for unit testing the subscription loop's failure handling. Once every script has been played,
/// a subscription waits for cancellation, like an idle live subscription.
/// </summary>
/// <param name="isTransient">Classifies failures; every failure is transient when omitted.</param>
sealed class ScriptedSubscriptionFactory(Func<Exception, bool>? isTransient = null) : ISubscriptionFactory {
    readonly Lock _lock = new();
    readonly Queue<Func<IAsyncEnumerable<SubscriptionMessage>>> _scripts = new();
    readonly List<ulong?> _fromPositions = [];
    TaskCompletionSource _idle = NewSignal();

    /// <summary>
    /// A message at <paramref name="position"/> whose event is the position itself.
    /// </summary>
    public static SubscriptionMessage Message(ulong position) => new(position, position, EventMetadata.Empty);

    /// <summary>
    /// A message at <paramref name="position"/> that could not be deserialized.
    /// </summary>
    public static SubscriptionMessage Undeserializable(ulong position, Exception error) =>
        new(null, position, EventMetadata.Empty) { DeserializationError = error };

    /// <summary>
    /// The <c>fromPosition</c> of every call to <see cref="Subscribe"/>, in order.
    /// </summary>
    public IReadOnlyList<ulong?> FromPositions {
        get { lock (_lock) return [.. _fromPositions]; }
    }

    /// <summary>
    /// Completes when a subscription has played its script and waits for cancellation.
    /// </summary>
    public Task Idle {
        get { lock (_lock) return _idle.Task; }
    }

    /// <summary>
    /// The next subscription yields <paramref name="messages"/> and then waits for cancellation.
    /// </summary>
    public ScriptedSubscriptionFactory Then(params SubscriptionMessage[] messages) =>
        Enqueue(messages, end: null);

    /// <summary>
    /// The next subscription yields <paramref name="messages"/> and then throws <paramref name="failure"/>.
    /// </summary>
    public ScriptedSubscriptionFactory ThenFail(Exception failure, params SubscriptionMessage[] messages) =>
        Enqueue(messages, end: () => throw failure);

    /// <summary>
    /// The next subscription yields <paramref name="messages"/> and then ends without failure.
    /// </summary>
    public ScriptedSubscriptionFactory ThenEnd(params SubscriptionMessage[] messages) =>
        Enqueue(messages, end: () => { });

    /// <summary>
    /// The next call to <see cref="Subscribe"/> throws <paramref name="failure"/>.
    /// </summary>
    public ScriptedSubscriptionFactory ThenFailToSubscribe(Exception failure) {
        lock (_lock) _scripts.Enqueue(() => throw failure);
        return this;
    }

    /// <inheritdoc/>
    public ISubscription Subscribe(ulong? fromPosition, bool startFromEnd, CancellationToken cancellationToken = default) {
        Func<IAsyncEnumerable<SubscriptionMessage>>? script;
        lock (_lock) {
            _fromPositions.Add(fromPosition);
            _scripts.TryDequeue(out script);
        }
        return new Subscription(script?.Invoke() ?? WaitAsync([]));
    }

    /// <inheritdoc/>
    public bool IsTransient(Exception exception) => isTransient?.Invoke(exception) ?? true;

    ScriptedSubscriptionFactory Enqueue(SubscriptionMessage[] messages, Action? end) {
        lock (_lock) _scripts.Enqueue(() => end is null ? WaitAsync(messages) : PlayAsync(messages, end));
        return this;
    }

    static async IAsyncEnumerable<SubscriptionMessage> PlayAsync(SubscriptionMessage[] messages, Action end) {
        foreach (var message in messages) {
            await Task.Yield();
            yield return message;
        }
        end();
    }

    async IAsyncEnumerable<SubscriptionMessage> WaitAsync(SubscriptionMessage[] messages,
        [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken cancellationToken = default) {
        foreach (var message in messages) {
            await Task.Yield();
            yield return message;
        }

        TaskCompletionSource idle;
        lock (_lock) {
            idle = _idle;
            _idle = NewSignal();
        }
        idle.TrySetResult();
        await Task.Delay(Timeout.Infinite, cancellationToken);
    }

    static TaskCompletionSource NewSignal() => new(TaskCreationOptions.RunContinuationsAsynchronously);

    sealed class Subscription(IAsyncEnumerable<SubscriptionMessage> messages) : ISubscription {
        public IAsyncEnumerator<SubscriptionMessage> GetAsyncEnumerator(CancellationToken cancellationToken = default) =>
            messages.GetAsyncEnumerator(cancellationToken);

        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }
}
