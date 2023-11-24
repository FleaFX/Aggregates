namespace Aggregates.Projections; 

/// <summary>
/// Defers the creation of the commit until the projection is ready to be committed..
/// </summary>
/// <typeparam name="TState">The state of the projection.</typeparam>
/// <typeparam name="TCommit">The type of the commit to produce.</typeparam>
/// <param name="Origin">The originating state, to be returned after committing the changes.</param>
/// <param name="AsyncCommitFactory">A function that asynchronously produces the <see cref="ICommit{TState}"/> to execute.</param>
sealed record DeferredCommit<TState, TCommit>(TState Origin, Func<TState, CancellationToken, ValueTask<TCommit>> AsyncCommitFactory) : ICommit<TState>
    where TCommit : ICommit<TState> {

    /// <summary>
    /// Asynchronously commits the changes made to a projection after applying an event.
    /// </summary>
    /// <param name="cancellationToken">A <see cref="CancellationToken"/> to cancel the asynchronous operation.</param>
    /// <returns>A <see cref="ValueTask"/> that represents the asynchronous operation, which resolves to the new state.</returns>
    async ValueTask<TState> ICommit<TState>.CommitAsync(CancellationToken cancellationToken) =>
        await (await AsyncCommitFactory(Origin, cancellationToken)).CommitAsync(cancellationToken);

}