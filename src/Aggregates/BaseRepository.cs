using Aggregates.Entities;
using Aggregates.Sagas;

namespace Aggregates;

/// <summary>
/// Repository base class.
/// </summary>
/// <typeparam name="TState">The type of the state object.</typeparam>
/// <typeparam name="TEvent">The type of the event object.</typeparam>
public abstract class BaseRepository<TState, TEvent> : IRepository<TState, TEvent> where TState : IState<TState, TEvent> {
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
    /// <returns>An awaitable <see cref="ValueTask{TResult}"/>, which resolves to a <see cref="EntityRoot{TState,TEvent}"/>.</returns>
    public async ValueTask<EntityRoot<TState, TEvent>?> TryGetEntityRootAsync(AggregateIdentifier identifier) {
        EntityRoot<TState, TEvent>? FromUow() {
            var aggregate = _unitOfWork.Get(identifier);
            return aggregate is { AggregateRoot: { } aggregateRoot }
                ? (EntityRoot<TState, TEvent>)aggregateRoot
                : null;
        }

        async ValueTask<EntityRoot<TState, TEvent>?> FromCore() {
            var aggregate = await GetEntityCoreAsync(identifier);
            if (aggregate is { } aggregateRoot)
                _unitOfWork.Attach(new Aggregate(identifier, aggregateRoot));
            return aggregate;
        }

        return FromUow() ?? await FromCore();
    }

    /// <summary>
    /// Asynchronously adds the given <paramref name="entityRoot"/> to the repository and associates it with the given <paramref name="identifier"/>.
    /// </summary>
    /// <param name="identifier">Uniquely identifies the aggregate.</param>
    /// <param name="entityRoot">The <see cref="EntityRoot{TState,TEvent}"/> to add.</param>
    /// <returns>An awaitable <see cref="ValueTask"/>.</returns>
    public void Add(AggregateIdentifier identifier, EntityRoot<TState, TEvent> entityRoot) =>
        _unitOfWork.Attach(new Aggregate(identifier, entityRoot));

    /// <summary>
    /// Asynchronously attempts to retrieve the <see cref="SagaRoot{TState,TEvent}"/> associated with the given <paramref name="identifier"/>.
    /// </summary>
    /// <param name="identifier">Uniquely identifies the aggregate to retrieve.</param>
    /// <returns>An awaitable <see cref="ValueTask{TResult}"/>, which resolves to a <see cref="SagaRoot{TState,TEvent}"/> or <see langword="null"/> if it wasn't found.</returns>
    public async ValueTask<SagaRoot<TState, TEvent>?> TryGetSagaRootAsync(AggregateIdentifier identifier) {
        SagaRoot<TState, TEvent>? FromUow() {
            var aggregate = _unitOfWork.Get(identifier);
            return aggregate is { AggregateRoot: { } aggregateRoot }
                ? (SagaRoot<TState, TEvent>)aggregateRoot
                : null;
        }

        async ValueTask<SagaRoot<TState, TEvent>?> FromCore() {
            var aggregate = await GetSagaCoreAsync(identifier);
            if (aggregate is { } aggregateRoot)
                _unitOfWork.Attach(new Aggregate(identifier, aggregateRoot));
            return aggregate;
        }

        return FromUow() ?? await FromCore();
    }

    /// <summary>
    /// Adds the given <paramref name="sagaRoot"/> to the repository and associates it with the given <paramref name="identifier"/>.
    /// </summary>
    /// <param name="identifier">Uniquely identifies the aggregate.</param>
    /// <param name="sagaRoot">The <see cref="SagaRoot{TState,TEvent}"/> to add.</param>
    public void Add(AggregateIdentifier identifier, SagaRoot<TState, TEvent> sagaRoot) =>
        _unitOfWork.Attach(new Aggregate(identifier, sagaRoot));

    /// <summary>
    /// Asynchronously retrieves the <see cref="EntityRoot{TState,TEvent}"/> associated with the given <paramref name="identifier"/>.
    /// </summary>
    /// <param name="identifier">Uniquely identifies the aggregate to retrieve.</param>
    /// <returns>An awaitable <see cref="ValueTask{TResult}"/>, which resolves to a <see cref="EntityRoot{TState,TEvent}"/>.</returns>
    protected abstract ValueTask<EntityRoot<TState, TEvent>?> GetEntityCoreAsync(AggregateIdentifier identifier);

    /// <summary>
    /// Asynchronously retrieves the <see cref="SagaRoot{TState,TEvent}"/> associated with the given <paramref name="identifier"/>.
    /// </summary>
    /// <param name="identifier">Uniquely identifies the aggregate to retrieve.</param>
    /// <returns>An awaitable <see cref="ValueTask{TResult}"/>, which resolves to a <see cref="SagaRoot{TState,TEvent}"/>.</returns>
    protected abstract ValueTask<SagaRoot<TState, TEvent>?> GetSagaCoreAsync(AggregateIdentifier identifier);
}