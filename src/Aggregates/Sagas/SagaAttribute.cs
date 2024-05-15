namespace Aggregates.Sagas;

/// <summary>
/// For internal use. Use <see cref="SagaAttribute{TReactionEvent,TSagaIdProvider}"/> instead.
/// </summary>
public abstract class SagaAttribute : Attribute {
    /// <summary>
    /// Creates a <see cref="KeyValuePair"/> to be used in a metadata dictionary using the given <paramref name="event"/>.
    /// </summary>
    /// <param name="event">The event object that may provide more information to create the saga metadata.</param>
    /// <returns></returns>
    internal abstract KeyValuePair<string, object?> Create(object @event);
}

/// <summary>
/// Enriches an event with metadata that govern how the event is passed on to a saga.
/// </summary>
[AttributeUsage(AttributeTargets.Class | AttributeTargets.Struct, AllowMultiple = true)]
public class SagaAttribute<TReactionEvent, TSagaIdProvider>(string metadataKey) : SagaAttribute where TSagaIdProvider : ISagaIdProvider<TReactionEvent> {
    readonly Func<TReactionEvent, string> _sagaIdProvider = Activator.CreateInstance<TSagaIdProvider>().GetSagaId;

    /// <summary>
    /// Creates a <see cref="KeyValuePair"/> to be used in a metadata dictionary using the given <paramref name="event"/>.
    /// </summary>
    /// <param name="event">The event object that may provide more information to create the saga metadata.</param>
    /// <returns></returns>
    internal override KeyValuePair<string, object?> Create(object @event) =>
        new(metadataKey, new SagaMetadata(typeof(TReactionEvent).AssemblyQualifiedName!, _sagaIdProvider((TReactionEvent)@event)));
}