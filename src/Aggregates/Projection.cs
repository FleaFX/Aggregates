using System.Collections.Immutable;

namespace Aggregates;

/// <summary>
/// Base projection implementation that can be configured with different types of <see cref="ICommit{TState}"/> seeds.
/// </summary>
/// <typeparam name="TState">The type of the state that is maintained.</typeparam>
/// <typeparam name="TEvent">The type of the event that is being projected.</typeparam>
public abstract record Projection<TState, TEvent> : IProjection<TState, TEvent>
    where TState : Projection<TState, TEvent> {

    /// <summary>
    /// Collects a list of <see cref="ICommit{TState}"/> to perform.
    /// </summary>
    /// <param name="Origin"></param>
    /// <param name="Commits"></param>
    protected record Commit(TState Origin, ImmutableArray<ICommit<TState>> Commits) : ICommit<TState> {
        /// <summary>
        /// Produces a <see cref="Commit"/> that uses the given <paramref name="applicator"/> to produce the appropriate <see cref="ICommit{TState}"/>.
        /// </summary>
        /// <param name="applicator">A function that produces a <see cref="ICommit{TState}"/>.</param>
        /// <returns>A <see cref="Commit"/>.</returns>
        public Commit Use(Func<TState, ICommit<TState>> applicator) =>
            this with { Commits = Commits.Add(applicator(Origin)) };

        async ValueTask<TState> ICommit<TState>.CommitAsync(CancellationToken cancellationToken) {
            async ValueTask<TState> CommitAllAsync(ICommit<TState>[] commits, TState state) =>
                commits switch {
                    [var commit, ..] => await CommitAllAsync(commits[1..], await commit.CommitAsync(cancellationToken)),
                    [] => state
                };

            return await CommitAllAsync(Commits.ToArray(), Origin);
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
    /// Applies the given <paramref name="event"/> to progress to a new state.
    /// </summary>
    /// <param name="event">The event to apply.</param>
    /// <param name="metadata">A set of metadata that was saved with the event, if any.</param>
    /// <returns>The new state.</returns>
    public abstract ICommit<TState> Apply(TEvent @event, IReadOnlyDictionary<string, object?>? metadata = null);
}