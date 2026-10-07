using System.Collections.Concurrent;

namespace Aggregates.Testing;

/// <summary>
/// Records what test handlers and serializers did within one test host. Registered as a
/// singleton, and injected into scenario projections, policies and sagas.
/// </summary>
sealed class HandlerProbe {
    readonly ConcurrentQueue<(Type Handler, object Event, EventMetadata Metadata)> _calls = new();
    readonly ConcurrentDictionary<string, int> _deserialized = new();

    /// <summary>
    /// Records that <paramref name="handler"/> handled <paramref name="event"/>.
    /// </summary>
    public void Record(object handler, object @event, EventMetadata? metadata = null) =>
        _calls.Enqueue((handler.GetType(), @event, metadata ?? EventMetadata.Empty));

    /// <summary>
    /// The number of calls recorded for handlers of type <typeparamref name="THandler"/>.
    /// </summary>
    public int Count<THandler>() => _calls.Count(call => call.Handler == typeof(THandler));

    /// <summary>
    /// The number of calls recorded for handlers of type <paramref name="handlerType"/>.
    /// </summary>
    public int Count(Type handlerType) => _calls.Count(call => call.Handler == handlerType);

    /// <summary>
    /// The events recorded for handlers of type <paramref name="handlerType"/>, in order.
    /// </summary>
    public IReadOnlyList<object> Events(Type handlerType) =>
        [.. from call in _calls where call.Handler == handlerType select call.Event];

    /// <summary>
    /// The events recorded for handlers of type <typeparamref name="THandler"/>, in order.
    /// </summary>
    public IReadOnlyList<object> Events<THandler>() =>
        [.. from call in _calls where call.Handler == typeof(THandler) select call.Event];

    /// <summary>
    /// The metadata recorded for handlers of type <typeparamref name="THandler"/>, in order.
    /// </summary>
    public IReadOnlyList<EventMetadata> Metadata<THandler>() =>
        [.. from call in _calls where call.Handler == typeof(THandler) select call.Metadata];

    /// <summary>
    /// Records a call to the <c>Deserialize</c> delegate for <paramref name="eventType"/>.
    /// </summary>
    public void RecordDeserialize(string eventType) =>
        _deserialized.AddOrUpdate(eventType, 1, (_, count) => count + 1);

    /// <summary>
    /// The number of <c>Deserialize</c> calls per stored event type.
    /// </summary>
    public IReadOnlyDictionary<string, int> DeserializeCalls => _deserialized;
}
