using Aggregates.Entities;
using Aggregates.Sagas;
using Aggregates.Types;

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
        var aggregateRoot = await TryGetEntityRootAsync(identifier);
        if (aggregateRoot is null) throw new AggregateRootNotFoundException(identifier);
        return aggregateRoot.State;
    }

    #region Entities
    /// <summary>
    /// Asynchronously attempts to retrieve the <see cref="EntityRoot{TState,TEvent}"/> associated with the given <paramref name="identifier"/>.
    /// </summary>
    /// <param name="identifier">Uniquely identifies the aggregate to retrieve.</param>
    /// <returns>An awaitable <see cref="ValueTask{TResult}"/>, which resolves to a <see cref="EntityRoot{TState,TEvent}"/> or <see langword="null"/> if it wasn't found.</returns>
    internal ValueTask<EntityRoot<TState, TEvent>?> TryGetEntityRootAsync(AggregateIdentifier identifier);

    /// <summary>
    /// Asynchronously retrieves the <see cref="EntityRoot{TState,TEvent}"/> associated with the given <paramref name="identifier"/>.
    /// </summary>
    /// <param name="identifier">Uniquely identifies the aggregate root to retrieve.</param>
    /// <returns>An awaitable <see cref="ValueTask{TResult}"/>, which resolves to a <see cref="EntityRoot{TState,TEvent}"/>.</returns>
    internal async ValueTask<EntityRoot<TState, TEvent>> GetEntityRootAsync(AggregateIdentifier identifier) =>
        await TryGetEntityRootAsync(identifier) ?? throw new AggregateRootNotFoundException(identifier);

    /// <summary>
    /// Adds the given <paramref name="entityRoot"/> to the repository and associates it with the given <paramref name="identifier"/>.
    /// </summary>
    /// <param name="identifier">Uniquely identifies the aggregate.</param>
    /// <param name="entityRoot">The <see cref="EntityRoot{TState,TEvent}"/> to add.</param>
    internal void Add(AggregateIdentifier identifier, EntityRoot<TState, TEvent> entityRoot);
    #endregion

    #region Sagas
    /// <summary>
    /// Asynchronously attempts to retrieve the <see cref="SagaRoot{TState,TEvent}"/> associated with the given <paramref name="identifier"/>.
    /// </summary>
    /// <param name="identifier">Uniquely identifies the aggregate to retrieve.</param>
    /// <returns>An awaitable <see cref="ValueTask{TResult}"/>, which resolves to a <see cref="SagaRoot{TState,TEvent}"/> or <see langword="null"/> if it wasn't found.</returns>
    internal ValueTask<SagaRoot<TState, TEvent>> TryGetSagaRootAsync(AggregateIdentifier identifier);

    /// <summary>
    /// Asynchronously retrieves the <see cref="SagaRoot{TState,TEvent}"/> associated with the given <paramref name="identifier"/>.
    /// </summary>
    /// <param name="identifier">Uniquely identifies the aggregate root to retrieve.</param>
    /// <returns>An awaitable <see cref="ValueTask{TResult}"/>, which resolves to a <see cref="SagaRoot{TState,TEvent}"/>.</returns>
    internal async ValueTask<SagaRoot<TState, TEvent>> GetSagaRootAsync(AggregateIdentifier identifier) =>
        await TryGetSagaRootAsync(identifier) ?? throw new AggregateRootNotFoundException(identifier);

    /// <summary>
    /// Adds the given <paramref name="sagaRoot"/> to the repository and associates it with the given <paramref name="identifier"/>.
    /// </summary>
    /// <param name="identifier">Uniquely identifies the aggregate.</param>
    /// <param name="sagaRoot">The <see cref="SagaRoot{TState,TEvent}"/> to add.</param>
    internal void Add(AggregateIdentifier identifier, SagaRoot<TState, TEvent> sagaRoot);
    #endregion
}