using System.Text.Json;
using Aggregates.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace Aggregates.Sagas.Handlers;

/// <summary>
/// Handler that decides whether a given event creates a new or affects an existing <see cref="SagaRoot{TState,TEvent}"/> object and acts accordingly.
/// </summary>
/// <typeparam name="TReactionState">The type of the state object that supports the reaction logic.</typeparam>
/// <typeparam name="TReactionEvent">The type of the event to react to.</typeparam>
/// <typeparam name="TCommand">The type of the produced commands.</typeparam>
/// <typeparam name="TCommandState">The type of the state object that is acted upon by the produced commands.</typeparam>
/// <typeparam name="TCommandEvent">The type of the event(s) that are applicable to the state that is acted upon by the produced commands.</typeparam>
/// <remarks>
/// Initializes a new <see cref="DefaultHandler{TReactionState,TReactionEvent,TCommand,TCommandState,TCommandEvent}"/>.
/// </remarks>
/// <param name="repository">The <see cref="IRepository{TState,TEvent}"/> to use when interacting with the aggregate which is affected by the handled event.</param>
/// <param name="reaction">The <see cref="IReaction{TReactionState, TReactionEvent,TCommand,TState,TEvent}"/> that reacts to the given event.</param>
/// <param name="serviceScopeFactory">The <see cref="IServiceScopeFactory"/> to use when creating scopes for the required command handlers.</param>
/// <param name="aggregatesOptions">Configures the behaviour of the handler.</param>
class DefaultHandler<TReactionState, TReactionEvent, TCommand, TCommandState, TCommandEvent>(
    IRepository<TReactionState, TReactionEvent> repository,
    IReaction<TReactionState, TReactionEvent, TCommand, TCommandState, TCommandEvent> reaction,
    IServiceScopeFactory serviceScopeFactory,
    AggregatesOptions aggregatesOptions
    ) : ISagaHandler<TReactionState, TReactionEvent, TCommand, TCommandState, TCommandEvent>
    where TReactionState : IState<TReactionState, TReactionEvent>
    where TCommand : ICommand<TCommandState, TCommandEvent>
    where TCommandState : IState<TCommandState, TCommandEvent> {
    readonly string _sagaIdMetadataKey = aggregatesOptions.SagaKey;

    /// <summary>
    /// Asynchronously handles the given <paramref name="event"/>.
    /// </summary>
    /// <param name="event">The event to handle.</param>
    /// <param name="metadata">The metadata associated with the event.</param>
    /// <param name="cancellationToken">A <see cref="CancellationToken"/> to cancel the asynchronous operation.</param>
    /// <returns>A <see cref="ValueTask"/> that represents the asynchronous operation.</returns>
    public async ValueTask HandleAsync(TReactionEvent @event, IReadOnlyDictionary<string, object?>? metadata, CancellationToken cancellationToken) {
        if (metadata is null || !metadata.TryGetValue(_sagaIdMetadataKey, out var objSagaMetadata) || objSagaMetadata is not JsonElement jsonSagaMetadata) return;

        var sagaId = jsonSagaMetadata.ValueKind switch {
            JsonValueKind.Object => jsonSagaMetadata.Deserialize<SagaMetadata>()?.SagaId ?? string.Empty,
            JsonValueKind.Array => (
                    from meta in jsonSagaMetadata.Deserialize<SagaMetadata[]>()
                    let eventType = Type.GetType(meta.EventType)
                    where eventType == typeof(TReactionEvent)
                    select meta.SagaId
                ).FirstOrDefault() ?? string.Empty,
            _ => string.Empty
        };

        var aggregateRoot = await repository.TryGetSagaRootAsync(sagaId);
        if (aggregateRoot is null) {
            aggregateRoot = new SagaRoot<TReactionState, TReactionEvent>(TReactionState.Initial, AggregateVersion.None);
            repository.Add(sagaId, aggregateRoot);
        }

        await foreach (var command in aggregateRoot.AcceptAsync(reaction, @event, metadata, cancellationToken)) {
            using var scope = serviceScopeFactory.CreateScope();
            var commandHandler = scope.ServiceProvider.GetRequiredService<ICommandHandler<TCommand, TCommandState, TCommandEvent>>();
            await commandHandler.HandleAsync(command);
        }
    }
}