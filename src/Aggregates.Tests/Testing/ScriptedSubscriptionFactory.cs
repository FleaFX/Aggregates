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
    readonly List<SubscriptionRequest> _requests = [];
    TaskCompletionSource _idle = NewSignal();
    int _endPositionCalls;

    /// <summary>
    /// A message at <paramref name="position"/> whose event is the position itself.
    /// </summary>
    public static SubscriptionMessage Message(ulong position) => new(position, position, new EventReference("stream-1", position), EventMetadata.Empty);

    /// <summary>
    /// A message at <paramref name="position"/> that could not be deserialized.
    /// </summary>
    public static SubscriptionMessage Undeserializable(ulong position, Exception error) =>
        new(null, position, new EventReference("stream-1", position), EventMetadata.Empty) { DeserializationError = error };

    /// <summary>
    /// The request of every call to <see cref="Subscribe"/>, in order.
    /// </summary>
    public IReadOnlyList<SubscriptionRequest> Requests {
        get { lock (_lock) return [.. _requests]; }
    }

    /// <summary>
    /// The <c>FromPosition</c> of every call to <see cref="Subscribe"/>, in order.
    /// </summary>
    public IReadOnlyList<ulong?> FromPositions {
        get { lock (_lock) return [.. _requests.Select(r => r.FromPosition)]; }
    }

    /// <summary>
    /// The end position that <see cref="ISubscriptionFactory.GetEndPositionAsync"/> returns.
    /// </summary>
    public ulong? EndPosition { get; set; }

    /// <summary>
    /// How often <see cref="ISubscriptionFactory.GetEndPositionAsync"/> was called.
    /// </summary>
    public int EndPositionCalls { get { lock (_lock) return _endPositionCalls; } }

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
    /// The next subscription yields <paramref name="messages"/>, waits for cancellation like
    /// <see cref="Then"/>, and then throws <paramref name="failure"/> instead of an
    /// <see cref="OperationCanceledException"/>, or ends without failure when
    /// <paramref name="failure"/> is <see langword="null"/>.
    /// </summary>
    public ScriptedSubscriptionFactory ThenOnStop(Exception? failure, params SubscriptionMessage[] messages) =>
        Enqueue(messages, end: null, onStop: () => { if (failure is { } toThrow) throw toThrow; });

    /// <summary>
    /// The next call to <see cref="Subscribe"/> throws <paramref name="failure"/>.
    /// </summary>
    public ScriptedSubscriptionFactory ThenFailToSubscribe(Exception failure) {
        lock (_lock) _scripts.Enqueue(() => throw failure);
        return this;
    }

    /// <inheritdoc/>
    public ISubscription Subscribe(SubscriptionRequest request, CancellationToken cancellationToken = default) {
        Func<IAsyncEnumerable<SubscriptionMessage>>? script;
        lock (_lock) {
            _requests.Add(request);
            _scripts.TryDequeue(out script);
        }
        return new Subscription(script?.Invoke() ?? WaitAsync([]));
    }

    /// <inheritdoc/>
    public ValueTask<ulong?> GetEndPositionAsync(CancellationToken cancellationToken = default) {
        lock (_lock) _endPositionCalls++;
        return ValueTask.FromResult(EndPosition);
    }

    /// <inheritdoc/>
    public bool IsTransient(Exception exception) => isTransient?.Invoke(exception) ?? true;

    ScriptedSubscriptionFactory Enqueue(SubscriptionMessage[] messages, Action? end, Action? onStop = null) {
        lock (_lock) _scripts.Enqueue(() => end is null ? WaitAsync(messages, onStop) : PlayAsync(messages, end));
        return this;
    }

    static async IAsyncEnumerable<SubscriptionMessage> PlayAsync(SubscriptionMessage[] messages, Action end) {
        foreach (var message in messages) {
            await Task.Yield();
            yield return message;
        }
        end();
    }

    async IAsyncEnumerable<SubscriptionMessage> WaitAsync(SubscriptionMessage[] messages, Action? onStop = null,
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
        try {
            await Task.Delay(Timeout.Infinite, cancellationToken);
        } catch (OperationCanceledException) when (onStop is { } onCancellation) {
            onCancellation();
        }
    }

    static TaskCompletionSource NewSignal() => new(TaskCreationOptions.RunContinuationsAsynchronously);

    sealed class Subscription(IAsyncEnumerable<SubscriptionMessage> messages) : ISubscription {
        public IAsyncEnumerator<SubscriptionMessage> GetAsyncEnumerator(CancellationToken cancellationToken = default) =>
            messages.GetAsyncEnumerator(cancellationToken);

        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }
}
