using EventStore.Client;

namespace Aggregates.EventStoreDB; 

class EventStoreDBRepository<TState, TEvent> : BaseRepository<TState, TEvent> where TState : IState<TState, TEvent> {
    readonly EventStoreClient _eventStoreClient;
    readonly ResolvedEventDeserializer _deserializer;

    /// <summary>
    /// Initializes a new <see cref="EventStoreDBRepository{TState,TEvent}"/>.
    /// </summary>
    /// <param name="unitOfWork">The <see cref="UnitOfWork"/> to track changes.</param>
    /// <param name="eventStoreClient">The <see cref="EventStoreClient"/> to use.</param>
    /// <param name="deserializer">A <see cref="ResolvedEventDeserializer"/> that deserializes a <see cref="ResolvedEvent"/>.</param>
    public EventStoreDBRepository(UnitOfWork unitOfWork, EventStoreClient eventStoreClient, ResolvedEventDeserializer deserializer) : base(unitOfWork) {
        _eventStoreClient = eventStoreClient ?? throw new ArgumentNullException(nameof(eventStoreClient));
        _deserializer = deserializer ?? throw new ArgumentNullException(nameof(deserializer));
    }

    /// <summary>
    /// Asynchronously retrieves the root of the aggregate associated with the given <paramref name="identifier"/>.
    /// </summary>
    /// <param name="identifier">Uniquely identifies the aggregate to retrieve.</param>
    /// <returns>An awaitable <see cref="ValueTask{TResult}"/>, which resolves to a <see cref="AggregateRoot{TState,TEvent}"/>.</returns>
    protected override async ValueTask<AggregateRoot<TState, TEvent>?> GetCoreAsync(AggregateIdentifier identifier) {
        try {
            var events = _eventStoreClient.ReadStreamAsync(Direction.Forwards, identifier.Value, StreamPosition.Start);
            var state = await events.Select(_deserializer.Deserialize).Cast<TEvent>().AggregateAsync(TState.Initial, (state, @event) => state.Apply(@event));

            return new AggregateRoot<TState, TEvent>(state, events.LastStreamPosition?.ToInt64() ?? 0L);
        }
        catch (StreamNotFoundException) {
            return null;
        }
    }
}