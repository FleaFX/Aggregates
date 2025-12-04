using Aggregates.EventStoreDB.Extensions;
using EventStore.Client;
using Microsoft.Extensions.DependencyInjection;
using System.Reflection;
using Aggregates.EventStoreDB.Serialization;
using Aggregates.EventStoreDB.Util;
using Aggregates.Metadata;
using Grpc.Core;
using Microsoft.Extensions.Logging;
using Aggregates.Configuration;
using Aggregates.Policies;

namespace Aggregates.EventStoreDB.Workers;

/// <summary>
/// Initializes a new <see cref="PolicyWorker{TPolicy,TPolicyEvent,TCommand,TState,TEvent}"/>.
/// </summary>
/// <param name="serviceScopeFactory">A <see cref="IServiceScopeFactory"/> that creates a scope in order to resolve the dependencies.</param>
class PolicyWorker<TPolicy, TPolicyEvent, TCommand, TState, TEvent>(IServiceScopeFactory serviceScopeFactory, ILogger<PolicyWorker<TPolicy, TPolicyEvent, TCommand, TState, TEvent>> logger)
    : ScopedBackgroundService<ListToAllAsyncDelegate, CreateToAllAsyncDelegate, DeleteToAllAsyncDelegate, SubscribeToAll, ResolvedEventDeserializer, MetadataDeserializer, AggregatesOptions, TPolicy>(serviceScopeFactory)
    where TState : IState<TState, TEvent>
    where TCommand : ICommand<TState, TEvent> {
    /// <summary>
    /// Implement this method rather than <see cref="ScopedBackgroundService{TDep1,TDep2,TDep3,TDep4,TDep5}.ExecuteAsync"/>. The required dependencies are passed in.
    /// </summary>
    /// <param name="listToAllAsync">Asynchronously lists persistent subscriptions to $all.</param>
    /// <param name="createToAllAsync">Asynchronously creates a filtered persistent subscription to $all.</param>
    /// <param name="subscribeToAll">Asynchronously subscribes to a persistent subscription to $all.</param>
    /// <param name="deserializer">Deserializes a <see cref="ResolvedEvent"/>.</param>
    /// <param name="owner">The reaction to work.</param>
    /// <param name="stoppingToken">A <see cref="CancellationToken"/> that is signaled when the asynchronous operation should be stopped.</param>
    /// <returns>A <see cref="Task"/> that represents the asynchronous operation.</returns>
    protected override async Task ExecuteCoreAsync(
        ListToAllAsyncDelegate listToAllAsync,
        CreateToAllAsyncDelegate createToAllAsync,
        DeleteToAllAsyncDelegate deleteToAllAsync,
        SubscribeToAll subscribeToAll,
        ResolvedEventDeserializer deserializer,
        MetadataDeserializer metadataDeserializer,
        AggregatesOptions options,
        TPolicy owner,
        CancellationToken stoppingToken) {

        var (subscriptionGroupName, @delegate, policyHandler, skipPosition) = await BootstrapAsync(owner, ServiceScopeFactory, logger, listToAllAsync, createToAllAsync, deleteToAllAsync, options, stoppingToken);
        if (owner is null || @delegate is null)
            return;

        policyHandler ??= new FailFastHandler<TCommand, TState, TEvent>(ServiceScopeFactory);

        // now connect the subscription and start updating the projection state
        await Task.Run(async () => {
            do {
                await using var subscription = subscribeToAll(subscriptionGroupName);

                try {
                    await foreach (var message in subscription.Messages.WithCancellation(stoppingToken)) {
                        switch (message) {
                            case PersistentSubscriptionMessage.Event @event when !Equals(@event.ResolvedEvent.OriginalPosition, skipPosition): {
                                // react to the event and handle each command
                                try {
                                    logger.LogInformation("Received event {eventType} @ {position} in {subscriptionGroupName}", @event.ResolvedEvent.Event.EventType, @event.ResolvedEvent.Event.Position, subscriptionGroupName);

                                    await using var metadataScope = new MetadataScope();
                                    var metadata = metadataDeserializer.Deserialize(@event.ResolvedEvent);
                                    foreach (var pair in metadata ?? new Dictionary<string, object?>()) {
                                        metadataScope.Add(pair);
                                    }

                                    var batch = await @delegate((TPolicyEvent)deserializer.Deserialize(@event.ResolvedEvent), metadata, stoppingToken).ToArrayAsync(cancellationToken: stoppingToken);

                                    policyHandler.Reset();
                                    foreach (var command in batch) {
                                        await policyHandler.HandleAsync(@event, subscriptionGroupName, command, batch.Length);
                                    }

                                    // notify EventStoreDB that we're done
                                    await subscription.Ack(@event.ResolvedEvent);

                                    logger.LogInformation("Ack'ed event {eventType} @ {position} in {subscriptionGroupName}", @event.ResolvedEvent.Event.EventType, @event.ResolvedEvent.Event.Position, subscriptionGroupName);
                                }
                                catch (Exception ex) {
                                    logger.LogError(ex, "Exception occurred during handling of {eventType} @ {position} in subscription {subscriptionGroupName}.", @event.ResolvedEvent.Event.EventType, @event.ResolvedEvent.Event.Position, subscriptionGroupName);
                                    await subscription.Nack(
                                        @event.RetryCount < 5
                                            ? PersistentSubscriptionNakEventAction.Retry
                                            : PersistentSubscriptionNakEventAction.Park, ex.Message,
                                        @event.ResolvedEvent);
                                }
                                break;
                            }

                            case PersistentSubscriptionMessage.Event @event: {
                                await subscription.Ack(@event.ResolvedEvent);
                                break;
                            }

                            case PersistentSubscriptionMessage.SubscriptionConfirmation confirmation:
                                logger.LogInformation($"Subscription to {confirmation.SubscriptionId} has been confirmed. Policy started.");
                                break;
                        }
                    }
                }
                catch (RpcException e) {
                    logger.LogError(e, e?.Message);
                }

                if (!stoppingToken.IsCancellationRequested) logger.LogWarning("Subscription has ended or has been dropped. Reconnecting.");
            } while (!stoppingToken.IsCancellationRequested);
        }, stoppingToken);

        await stoppingToken;
    }

    static async Task<(string? subscriptionGroupName, PolicyAsyncDelegate<TPolicyEvent, TCommand, TState, TEvent>? @delegate, PolicyErrorModeAwareCommandHandler<TCommand, TState, TEvent>? policyHandler, IPosition? skipPosition)> BootstrapAsync(TPolicy owner, IServiceScopeFactory serviceScopeFactory, ILogger logger, ListToAllAsyncDelegate listToAllAsync, CreateToAllAsyncDelegate createToAllAsync, DeleteToAllAsyncDelegate deleteToAllAsync, AggregatesOptions options, CancellationToken cancellationToken) {
        var ownerType = owner!.GetType();
        if (ownerType.GetCustomAttribute<PolicyContractAttribute>() is not { } policyContract)
            return (null, null, null, null);

        // find policy delegate
        var @delegate = (
                from method in ownerType.GetMethods(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.DeclaredOnly)
                where method.IsDelegate<PolicyAsyncDelegate<TPolicyEvent, TCommand, TState, TEvent>>(owner)
                select method.CreateDelegate<PolicyAsyncDelegate<TPolicyEvent, TCommand, TState, TEvent>>(owner)
            ).Concat(
                from method in ownerType.GetMethods(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.DeclaredOnly)
                where method.IsDelegate<PolicyDelegate<TPolicyEvent, TCommand, TState, TEvent>>(owner)
                let dlgt = method.CreateDelegate<PolicyDelegate<TPolicyEvent, TCommand, TState, TEvent>>(owner)
                select new PolicyAsyncDelegate<TPolicyEvent, TCommand, TState, TEvent>((@event, metadata, _) => dlgt(@event, metadata).ToAsyncEnumerable())
            ).SingleOrDefault();
        if (@delegate is null)
            return (null, null, null, null);

        PolicyErrorModeAwareCommandHandler<TCommand, TState, TEvent> policyHandler = policyContract.ErrorHandlingMode switch {
            PolicyErrorHandlingMode.FailFast => new FailFastHandler<TCommand, TState, TEvent>(serviceScopeFactory),
            PolicyErrorHandlingMode.ContinueOnError => new ContinueOnErrorHandler<TCommand, TState, TEvent>(serviceScopeFactory, logger),
            PolicyErrorHandlingMode.ContinueUntilMaxErrors => new ContinueUntilMaxErrorsHandler<TCommand, TState, TEvent>(serviceScopeFactory, logger, policyContract.MaxErrors),
            PolicyErrorHandlingMode.ContinueUntilMaxFailureRate => new ContinueUntilMaxFailureRateHandler<TCommand, TState, TEvent>(serviceScopeFactory, logger, policyContract.MaxErrorRate),
            _ => throw new ArgumentOutOfRangeException()
        };

        // find subscription and create it if it doesn't exist
        var subscription = (await listToAllAsync(cancellationToken: cancellationToken)).FirstOrDefault(info => info.GroupName == policyContract.ToString());
        if (subscription != null) return (subscription.GroupName, @delegate, policyHandler, null);

        // find applicable event types by tentatively applying them to the state
        var eventTypes = (
            from assembly in options.Assemblies ?? AppDomain.CurrentDomain.GetAssemblies()
            from type in assembly.GetTypes()
            let attr = type.GetCustomAttribute<EventContractAttribute>()
            where type.IsAssignableTo(typeof(TPolicyEvent)) && attr != null
            select (type, attr)
        ).TrySelect(tuple => {
            var (eventType, contract) = tuple;
            _ = @delegate((TPolicyEvent)Activator.CreateInstance(eventType)!, cancellationToken: cancellationToken).ToBlockingEnumerable().ToArray();
            return contract;
        });

        // create a persistent subscription with a filter on event type
        var filter = string.Join('|', eventTypes.Select(eventType => eventType.ToString().Replace(".", @"\.")));

        // find the preceding subscription, if any
        IPosition position = policyContract.StartFromEnd ? Position.End : Position.Start;
        IPosition? skipPosition = null;
        if (policyContract.ContinueFrom is { } continueFrom && (await listToAllAsync(cancellationToken: cancellationToken)).FirstOrDefault(info => info.GroupName == continueFrom) is { } preceding) {
            skipPosition = preceding.Stats.LastKnownEventPosition;

            // use its last known event position as the new starting point, otherwise just start from the beginning
            position = preceding.Stats.LastKnownEventPosition ?? position;

            // delete the preceding subscription
            await deleteToAllAsync(preceding.GroupName, cancellationToken: cancellationToken);
        }

        // now create the new subscription
        await createToAllAsync(policyContract.ToString(), EventTypeFilter.RegularExpression($"^(?:{filter})$"), new PersistentSubscriptionSettings(startFrom: position), cancellationToken: cancellationToken);

        return (policyContract.ToString(), @delegate, policyHandler, skipPosition);
    }
}

abstract class PolicyErrorModeAwareCommandHandler<TCommand, TState, TEvent>(IServiceScopeFactory serviceScopeFactory)
    where TState : IState<TState, TEvent>
    where TCommand : ICommand<TState, TEvent> {
    public abstract void Reset();

    public async ValueTask HandleAsync(PersistentSubscriptionMessage.Event @event, string? subscriptionGroupName, TCommand command, int batchSize) {
        using var scope = serviceScopeFactory.CreateScope();
        var commandHandler = scope.ServiceProvider.GetRequiredService<ICommandHandler<TCommand, TState, TEvent>>();
        await HandleCoreAsync(@event, subscriptionGroupName, command, commandHandler.HandleAsync, batchSize);
    }

    protected abstract ValueTask HandleCoreAsync(PersistentSubscriptionMessage.Event @event, string? subscriptionGroupName, TCommand command, Func<TCommand, ValueTask> asyncHandleFunc, int batchSize);
}

class FailFastHandler<TCommand, TState, TEvent>(IServiceScopeFactory serviceScopeFactory) : PolicyErrorModeAwareCommandHandler<TCommand, TState, TEvent>(serviceScopeFactory)
    where TState : IState<TState, TEvent>
    where TCommand : ICommand<TState, TEvent> {
    public override void Reset() { }

    protected override ValueTask HandleCoreAsync(PersistentSubscriptionMessage.Event @event, string? subscriptionGroupName, TCommand command, Func<TCommand, ValueTask> asyncHandleFunc, int batchSize) => asyncHandleFunc(command);
}

class ContinueOnErrorHandler<TCommand, TState, TEvent>(IServiceScopeFactory serviceScopeFactory, ILogger? logger) : PolicyErrorModeAwareCommandHandler<TCommand, TState, TEvent>(serviceScopeFactory)
    where TState : IState<TState, TEvent>
    where TCommand : ICommand<TState, TEvent> {
    public override void Reset() { }

    protected override async ValueTask HandleCoreAsync(PersistentSubscriptionMessage.Event @event, string? subscriptionGroupName, TCommand command, Func<TCommand, ValueTask> asyncHandleFunc, int batchSize) {
        try {
            await asyncHandleFunc(command);
        } catch (Exception ex) {
            logger?.LogError(
                ex, 
                "Exception occurred during handling of command {commandType} ({eventType} @ {position} in subscription {subscriptionGroupName}) but the policy is configured to continue on errors.", 
                typeof(TCommand),
                @event.ResolvedEvent.Event.EventType, 
                @event.ResolvedEvent.Event.Position, subscriptionGroupName
            );
        }
    }
}

class ContinueUntilMaxErrorsHandler<TCommand, TState, TEvent>(IServiceScopeFactory serviceScopeFactory, ILogger? logger, int maxErrors) : PolicyErrorModeAwareCommandHandler<TCommand, TState, TEvent>(serviceScopeFactory)
    where TState : IState<TState, TEvent>
    where TCommand : ICommand<TState, TEvent> {
    int _errors;

    public override void Reset() {
        _errors = 0;
    }

    protected override async ValueTask HandleCoreAsync(PersistentSubscriptionMessage.Event @event, string? subscriptionGroupName, TCommand command, Func<TCommand, ValueTask> asyncHandleFunc, int batchSize) {
        try {
            await asyncHandleFunc(command);
        } catch (Exception ex) {
            if (++_errors > maxErrors)
                throw;

            logger?.LogError(
                ex,
                "Exception occurred during handling of command {commandType} ({eventType} @ {position} in subscription {subscriptionGroupName}) but the policy is configured to tolerate {maxErrors} errors (currently at {errors} errors for this event)",
                typeof(TCommand),
                @event.ResolvedEvent.Event.EventType,
                @event.ResolvedEvent.Event.Position, subscriptionGroupName,
                maxErrors,
                _errors
            );
        }
    }
}

class ContinueUntilMaxFailureRateHandler<TCommand, TState, TEvent>(IServiceScopeFactory serviceScopeFactory, ILogger? logger, double maxFailureRate) : PolicyErrorModeAwareCommandHandler<TCommand, TState, TEvent>(serviceScopeFactory)
    where TState : IState<TState, TEvent>
    where TCommand : ICommand<TState, TEvent> {
    int _errors;

    public override void Reset() {
        _errors = 0;
    }

    protected override async ValueTask HandleCoreAsync(PersistentSubscriptionMessage.Event @event, string? subscriptionGroupName, TCommand command, Func<TCommand, ValueTask> asyncHandleFunc, int batchSize) {
        try {
            await asyncHandleFunc(command);
        } catch (Exception ex) {
            var rate = (double)++_errors / batchSize;

            if (rate > maxFailureRate + double.Epsilon)
                throw;

            logger?.LogError(
                ex,
                "Exception occurred during handling of command {commandType} ({eventType} @ {position} in subscription {subscriptionGroupName}) but the policy is configured to tolerate {maxErrors:P0} errors (currently at {errors:P0} errors for this event)",
                typeof(TCommand),
                @event.ResolvedEvent.Event.EventType,
                @event.ResolvedEvent.Event.Position, subscriptionGroupName,
                maxFailureRate,
                _errors
            );
        }
    }
}