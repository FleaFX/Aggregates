using Aggregates.Entities;
using Aggregates.EventStoreDB.Serialization;
using Aggregates.Sagas;
using Aggregates.Types;
using EventStore.Client;

namespace Aggregates.EventStoreDB;

class EventStoreDbRepository<TState, TEvent> : BaseRepository<TState, TEvent> where TState : IState<TState, TEvent> {
    readonly EventStoreClient _eventStoreClient;
    readonly ResolvedEventDeserializer _deserializer;

    /// <summary>
    /// Initializes a new <see cref="EventStoreDbRepository{TState,TEvent}"/>.
    /// </summary>
    /// <param name="unitOfWork">The <see cref="UnitOfWork"/> to track changes.</param>
    /// <param name="eventStoreClient">The <see cref="EventStoreClient"/> to use.</param>
    /// <param name="deserializer">A <see cref="ResolvedEventDeserializer"/> that deserializes a <see cref="ResolvedEvent"/>.</param>
    public EventStoreDbRepository(UnitOfWork unitOfWork, EventStoreClient eventStoreClient, ResolvedEventDeserializer deserializer) : base(unitOfWork) {
        _eventStoreClient = eventStoreClient ?? throw new ArgumentNullException(nameof(eventStoreClient));
        _deserializer = deserializer ?? throw new ArgumentNullException(nameof(deserializer));
    }

    /// <summary>
    /// Asynchronously retrieves the <see cref="EntityRoot{TState,TEvent}"/> associated with the given <paramref name="identifier"/>.
    /// </summary>
    /// <param name="identifier">Uniquely identifies the aggregate to retrieve.</param>
    /// <returns>An awaitable <see cref="ValueTask{TResult}"/>, which resolves to a <see cref="EntityRoot{TState,TEvent}"/>.</returns>
    protected override async ValueTask<EntityRoot<TState, TEvent>?> GetEntityCoreAsync(AggregateIdentifier identifier) {
        if (identifier.Value.StartsWith('$')) throw new InvalidOperationException("Repository shouldn't be reading a system stream.");

        try {
            var events = await _eventStoreClient.ReadStreamAsync(Direction.Forwards, identifier.Value, StreamPosition.Start).ToArrayAsync();
            var state = events.Select(_deserializer.Deserialize).Cast<TEvent>().Aggregate(TState.Initial, (state, @event) => state.Apply(@event));

            return new EntityRoot<TState, TEvent>(state, new AggregateVersion(events.Length - 1L));
        } catch (StreamNotFoundException) {
            return null;
        }
    }

    /// <summary>
    /// Asynchronously retrieves the <see cref="SagaRoot{TState,TEvent}"/> associated with the given <paramref name="identifier"/>.
    /// </summary>
    /// <param name="identifier">Uniquely identifies the aggregate to retrieve.</param>
    /// <returns>An awaitable <see cref="ValueTask{TResult}"/>, which resolves to a <see cref="EntityRoot{TState,TEvent}"/>.</returns>
    protected override async ValueTask<SagaRoot<TState, TEvent>?> GetSagaCoreAsync(AggregateIdentifier identifier) {
        if (identifier.Value.StartsWith('$')) throw new InvalidOperationException("Repository shouldn't be reading a system stream.");

        try {
            var events = await _eventStoreClient.ReadStreamAsync(Direction.Forwards, identifier.Value, StreamPosition.Start, resolveLinkTos: true).ToArrayAsync();
            var state = events.Select(_deserializer.Deserialize).Cast<TEvent>().Aggregate(TState.Initial, (state, @event) => state.Apply(@event));

            return new SagaRoot<TState, TEvent>(state, new AggregateVersion(events.Length - 1L));
        } catch (StreamNotFoundException) {
            return null;
        }
    }
}