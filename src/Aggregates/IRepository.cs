namespace Aggregates;

/// <summary>
/// Provides access to all aggregate root objects of <typeparamref name="TState"/>.
/// </summary>
/// <typeparam name="TState">The type of the maintained state object.</typeparam>
/// <typeparam name="TEvent">The type of the event(s) that are applicable.</typeparam>
public interface IRepository<TState, TEvent> where TState : IState<TState, TEvent> {
    /// <summary>
    /// Asynchronously retrieves the current state of the aggregate associated with the given <paramref name="identifier"/>.
    /// </summary>
    /// <param name="identifier">Uniquely identifies the aggregate to retrieve.</param>
    /// <returns>An awaitable <see cref="ValueTask{TResult}"/>, which resolves to a <see cref="AggregateRoot{TState,TEvent}"/>.</returns>
    async ValueTask<TState> GetAsync(AggregateIdentifier identifier) =>
        (await GetAggregateRootAsync(identifier));

    /// <summary>
    /// Asynchronously retrieves the root of the aggregate associated with the given <paramref name="identifier"/>.
    /// </summary>
    /// <param name="identifier">Uniquely identifies the aggregate to retrieve.</param>
    /// <returns>An awaitable <see cref="ValueTask{TResult}"/>, which resolves to a <see cref="AggregateRoot{TState,TEvent}"/>.</returns>
    internal ValueTask<AggregateRoot<TState, TEvent>> GetAggregateRootAsync(AggregateIdentifier identifier);

    /// <summary>
    /// Adds the given <paramref name="aggregateRoot"/> to the repository and associates it with the given <paramref name="identifier"/>.
    /// </summary>
    /// <param name="identifier">Uniquely identifies the aggregate.</param>
    /// <param name="aggregateRoot">The <see cref="AggregateRoot{TState,TEvent}"/> to add.</param>
    internal void Add(AggregateIdentifier identifier, AggregateRoot<TState, TEvent> aggregateRoot);
}

abstract class BaseRepository<TState, TEvent> : IRepository<TState, TEvent> where TState : IState<TState, TEvent> {
    readonly UnitOfWork _unitOfWork;

    /// <summary>
    /// Initializes a new <see cref="BaseRepository{TState,TEvent}"/>.
    /// </summary>
    /// <param name="unitOfWork">The <see cref="UnitOfWork"/> to track changes.</param>
    protected BaseRepository(UnitOfWork unitOfWork) => _unitOfWork = unitOfWork;
    
    /// <summary>
    /// Asynchronously retrieves the root of the aggregate associated with the given <paramref name="identifier"/>.
    /// </summary>
    /// <param name="identifier">Uniquely identifies the aggregate to retrieve.</param>
    /// <returns>An awaitable <see cref="ValueTask{TResult}"/>, which resolves to a <see cref="AggregateRoot{TState,TEvent}"/>.</returns>
    public async ValueTask<AggregateRoot<TState, TEvent>> GetAggregateRootAsync(AggregateIdentifier identifier) {
        AggregateRoot<TState, TEvent>? FromUow() {
            var aggregate = _unitOfWork.Get(identifier);
            return aggregate is { AggregateRoot: { } aggregateRoot }
                ? (AggregateRoot<TState, TEvent>)aggregateRoot
                : null;
        }

        async ValueTask<AggregateRoot<TState, TEvent>?> FromCore() {
            var aggregate = await GetCoreAsync(identifier);
            if (aggregate is { } aggregateRoot)
                _unitOfWork.Attach(new Aggregate(identifier, aggregateRoot));
            return aggregate;
        }

        return FromUow() ?? await FromCore() ?? throw new AggregateRootNotFoundException(identifier);
    }

    /// <summary>
    /// Asynchronously adds the given <paramref name="aggregateRoot"/> to the repository and associates it with the given <paramref name="identifier"/>.
    /// </summary>
    /// <param name="identifier">Uniquely identifies the aggregate.</param>
    /// <param name="aggregateRoot">The <see cref="AggregateRoot{TState,TEvent}"/> to add.</param>
    /// <returns>An awaitable <see cref="ValueTask"/>.</returns>
    public void Add(AggregateIdentifier identifier, AggregateRoot<TState, TEvent> aggregateRoot) =>
        _unitOfWork.Attach(new Aggregate(identifier, aggregateRoot));

    /// <summary>
    /// Asynchronously retrieves the root of the aggregate associated with the given <paramref name="identifier"/>.
    /// </summary>
    /// <param name="identifier">Uniquely identifies the aggregate to retrieve.</param>
    /// <returns>An awaitable <see cref="ValueTask{TResult}"/>, which resolves to a <see cref="AggregateRoot{TState,TEvent}"/>.</returns>
    protected abstract ValueTask<AggregateRoot<TState, TEvent>?> GetCoreAsync(AggregateIdentifier identifier);
}