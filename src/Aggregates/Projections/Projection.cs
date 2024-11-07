using System.Collections.Immutable;

namespace Aggregates.Projections;

/// <summary>
/// Base projection implementation that can be configured with different types of <see cref="ICommit{TState}"/> seeds.
/// </summary>
/// <typeparam name="TState">The type of the state that is maintained.</typeparam>
/// <typeparam name="TEvent">The type of the event that is being projected.</typeparam>
[Obsolete("Use the new ProjectionContract attribute instead.", false)]
public abstract record Projection<TState, TEvent> : IProjection<TState, TEvent>
    where TState : Projection<TState, TEvent> {

    /// <summary>
    /// Collects a list of <see cref="ICommit{TState}"/> to perform.
    /// </summary>
    /// <param name="Origin">The state object to return after all the commits have been executed.</param>
    /// <param name="Commits">The sequence of commits to execute.</param>
    protected record Commit(TState Origin, ImmutableArray<ICommit<TState>> Commits) : ICommit<TState> {
        /// <summary>
        /// Produces a <see cref="Commit"/> that uses the given <paramref name="applicator"/> to produce the appropriate <see cref="ICommit{TState}"/>.
        /// </summary>
        /// <param name="applicator">A function that produces a <see cref="ICommit{TState}"/>.</param>
        /// <returns>A <see cref="Commit"/>.</returns>
        public Commit Use(Func<TState, ICommit<TState>> applicator) =>
            this with { Commits = Commits.Add(applicator(Origin)) };

        /// <summary>
        /// Produces a <see cref="Commit"/> that adds a deferred commit to the sequence of commits to execute.
        /// </summary>
        /// <typeparam name="TCommit"></typeparam>
        /// <param name="factory"></param>
        /// <returns></returns>
        public Commit Use<TCommit>(Func<TState, CancellationToken, ValueTask<TCommit>> factory) where TCommit : ICommit<TState> =>
            Use(state => new DeferredCommit<TState, TCommit>(state, factory));

        async ValueTask<TState> ICommit<TState>.CommitAsync(CancellationToken cancellationToken) {
            return await CommitAllAsync(Commits.ToArray(), Origin);

            async ValueTask<TState> CommitAllAsync(ICommit<TState>[] commits, TState state) =>
                commits switch {
                    [var commit, ..] => await CommitAllAsync(commits[1..], await commit.CommitAsync(cancellationToken)),
                    [] => state
                };
        }
    }

    /// <summary>
    /// Produces a <see cref="Commit"/> that uses the given <paramref name="applicator"/> to produce the appropriate <see cref="ICommit{TState}"/>.
    /// </summary>
    /// <param name="applicator">A function that produces a <see cref="ICommit{TState}"/>.</param>
    /// <returns>A <see cref="Commit"/>.</returns>
    protected Commit Use(Func<TState, ICommit<TState>> applicator) =>
        new Commit((TState)this, ImmutableArray<ICommit<TState>>.Empty).Use(applicator);

    /// <summary>
    /// Defers the creation of the commit until the projection is ready to be committed.
    /// </summary>
    /// <typeparam name="TCommit">The type of the commit to create.</typeparam>
    /// <param name="factory">The factory that creates the <see cref="ICommit{TState}"/> to execute.</param>
    /// <returns>A <see cref="Commit"/>.</returns>
    protected Commit Use<TCommit>(Func<TState, CancellationToken, ValueTask<TCommit>> factory) where TCommit : ICommit<TState> =>
        Use(state => new DeferredCommit<TState, TCommit>(state, factory));

    /// <summary>
    /// Produces a <see cref="Commit"/> that doesn't change the state of the projection.
    /// </summary>
    /// <returns>A <see cref="Commit"/>.</returns>
    protected Commit Ignore() =>
        new((TState)this, ImmutableArray<ICommit<TState>>.Empty);

    /// <summary>
    /// Applies the given <paramref name="event"/> to progress to a new state.
    /// </summary>
    /// <param name="event">The event to apply.</param>
    /// <param name="metadata">A set of metadata that was saved with the event, if any.</param>
    /// <returns>The new state.</returns>
    public abstract ICommit<TState> Apply(TEvent @event, IReadOnlyDictionary<string, object?>? metadata = null);
}