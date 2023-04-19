// ReSharper disable CheckNamespace

using System.Runtime.CompilerServices;

namespace Aggregates.EventStoreDB;

static class ExtensionsForCancellationToken {
    /// <summary>
    /// Gets a <see cref="CancellationTokenAwaiter"/> that allows you to await the signaling of a <see cref="CancellationToken"/>.
    /// </summary>
    /// <param name="cancellationToken">The <see cref="CancellationToken"/> to await.</param>
    /// <returns>A <see cref="CancellationTokenAwaiter"/>.</returns>
    public static CancellationTokenAwaiter GetAwaiter(this CancellationToken cancellationToken) =>
        new CancellationTokenAwaiter(cancellationToken);
}

readonly struct CancellationTokenAwaiter : ICriticalNotifyCompletion {
    readonly CancellationToken _cancellationToken;

    /// <summary>
    /// Initializes a new <see cref="CancellationTokenAwaiter"/>.
    /// </summary>
    /// <param name="cancellationToken">The <see cref="CancellationToken"/> that signals completion when signaled.</param>
    public CancellationTokenAwaiter(CancellationToken cancellationToken) =>
        _cancellationToken = cancellationToken;

    public bool IsCompleted => _cancellationToken.IsCancellationRequested;

    public object GetResult() {
        if (IsCompleted) throw new OperationCanceledException();
        throw new InvalidOperationException("The cancellation token hasn't been cancelled yet.");
    }

    /// <summary>Schedules the continuation action that's invoked when the instance completes.</summary>
    /// <param name="continuation">The action to invoke when the operation completes.</param>
    /// <exception cref="T:System.ArgumentNullException">The <paramref name="continuation" /> argument is null (Nothing in Visual Basic).</exception>
    public void OnCompleted(Action continuation) => _cancellationToken.Register(continuation);

    /// <summary>Schedules the continuation action that's invoked when the instance completes.</summary>
    /// <param name="continuation">The action to invoke when the operation completes.</param>
    /// <exception cref="T:System.ArgumentNullException">The <paramref name="continuation" /> argument is null (Nothing in Visual Basic).</exception>
    public void UnsafeOnCompleted(Action continuation) => _cancellationToken.Register(continuation);
}