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
    /// <returns>An awaitable <see cref="ValueTask{TResult}"/>, which resolves to a <typeparamref name="TState"/>.</returns>
    /// <exception cref="AggregateRootNotFoundException">Thrown when the aggregate could not be found in the system.</exception>
    public async ValueTask<TState> GetAsync(AggregateIdentifier identifier) {
        var aggregateRoot = await TryGetAggregateRootAsync(identifier);
        if (aggregateRoot is null) throw new AggregateRootNotFoundException(identifier);
        return aggregateRoot.State;
    }
    

    /// <summary>
    /// Asynchronously attempts to retrieve the root of the aggregate associated with the given <paramref name="identifier"/>.
    /// </summary>
    /// <param name="identifier">Uniquely identifies the aggregate to retrieve.</param>
    /// <returns>An awaitable <see cref="ValueTask{TResult}"/>, which resolves to a <see cref="AggregateRoot{TState,TEvent}"/> or <see langword="null"/> if it wasn't found.</returns>
    internal ValueTask<AggregateRoot<TState, TEvent>?> TryGetAggregateRootAsync(AggregateIdentifier identifier);

    /// <summary>
    /// Asynchronously retrieves the root of the aggregate associated with the given <paramref name="identifier"/>.
    /// </summary>
    /// <param name="identifier">Uniquely identifies the aggregate to retrieve.</param>
    /// <returns>An awaitable <see cref="ValueTask{TResult}"/>, which resolves to a <see cref="AggregateRoot{TState,TEvent}"/>.</returns>
    internal async ValueTask<AggregateRoot<TState, TEvent>> GetAggregateRootAsync(AggregateIdentifier identifier) =>
        await TryGetAggregateRootAsync(identifier) ?? throw new AggregateRootNotFoundException(identifier);

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
    public async ValueTask<AggregateRoot<TState, TEvent>?> TryGetAggregateRootAsync(AggregateIdentifier identifier) {
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

        return FromUow() ?? await FromCore();
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