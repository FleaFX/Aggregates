using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace Aggregates.Subscriptions;

/// <summary>
/// The <see cref="IParkedMessages"/> of a host: lists parked messages from the parked streams, records
/// replays and dismissals there, and replays a message through the subscription that handles its id
/// in this host.
/// </summary>
/// <remarks>
/// A replay reads the event (or request) back from the store, deserializes it with the storage
/// integration's <c>Deserialize</c>, and hands it to the handler with the retries of
/// <see cref="SubscriptionRetryPolicy.TryExecuteAsync"/>; a deferred command goes to the
/// <see cref="ICommandDispatcher"/>, in a new scope and a metadata scope seeded with the metadata of its
/// request. Failures that retrying can't fix (no source, a deleted event, a type that doesn't
/// deserialize or that the handler doesn't take, no handler in this host) fail the replay right away.
/// </remarks>
/// <param name="subscriptions">The subscriptions of this host.</param>
/// <param name="retryPolicy">Retries a failing replay.</param>
/// <param name="scopeFactory">Creates the scope a deferred command is dispatched in.</param>
/// <param name="logger">The logger.</param>
/// <param name="store">The parked-message store of the storage integration; without one, every method throws.</param>
public sealed partial class ParkedMessages(
    IEnumerable<IReplayableSubscription> subscriptions,
    SubscriptionRetryPolicy retryPolicy,
    IServiceScopeFactory scopeFactory,
    ILogger<ParkedMessages> logger,
    IParkedMessageStore? store = null
) : IParkedMessages {

    /// <inheritdoc/>
    public async ValueTask<IReadOnlyList<ParkedMessage>> ListAsync(string subscriptionId, CancellationToken cancellationToken = default) {
        ArgumentException.ThrowIfNullOrEmpty(subscriptionId);
        var parked = new List<ParkedMessage>();
        var closed = new HashSet<string>(StringComparer.Ordinal);
        await foreach (var entry in Store.ReadParkedAsync(subscriptionId, cancellationToken)) {
            switch (entry.EventType) {
                case ParkedFormat.ParkedEventType:
                    parked.Add(ParkedFormat.ReadParked(subscriptionId, entry));
                    break;
                case ParkedFormat.ReplayedEventType or ParkedFormat.DismissedEventType
                    when ParkedFormat.TryReadClosed(subscriptionId, entry, out var id):
                    closed.Add(id);
                    break;
            }
        }
        return [.. parked.Where(message => !closed.Contains(message.Id))];
    }

    /// <inheritdoc/>
    public async ValueTask<ParkedMessageReplay> ReplayAsync(string subscriptionId, string id, CancellationToken cancellationToken = default) =>
        await ReplayAsync(await GetListedAsync(subscriptionId, id, cancellationToken), cancellationToken);

    /// <inheritdoc/>
    public async ValueTask<IReadOnlyList<ParkedMessageReplay>> ReplayAllAsync(string subscriptionId, CancellationToken cancellationToken = default) {
        var replays = new List<ParkedMessageReplay>();
        foreach (var message in await ListAsync(subscriptionId, cancellationToken)) {
            var replay = await ReplayAsync(message, cancellationToken);
            replays.Add(replay);
            // Later messages may depend on this one, and while the cause remains each would only wait for its retries.
            if (!replay.Succeeded)
                break;
        }
        return replays;
    }

    /// <inheritdoc/>
    public async ValueTask DismissAsync(string subscriptionId, string id, CancellationToken cancellationToken = default) {
        var message = await GetListedAsync(subscriptionId, id, cancellationToken);
        await Store.AppendParkedAsync(subscriptionId, ParkedFormat.DismissedEventType, ParkedFormat.WriteClosed(message), cancellationToken);
        LogDismissed(logger, message.Id, subscriptionId, message.Source);
    }

    IParkedMessageStore Store => store ?? throw new InvalidOperationException(
        "No parked-message store is configured: add a storage integration, such as AddKurrentDb() or AddMssp() " +
        "on the builder of the projections, policies or sagas.");

    /// <summary>
    /// The listed message with <paramref name="id"/>.
    /// </summary>
    async ValueTask<ParkedMessage> GetListedAsync(string subscriptionId, string id, CancellationToken cancellationToken) {
        ArgumentException.ThrowIfNullOrEmpty(id);
        return (await ListAsync(subscriptionId, cancellationToken)).FirstOrDefault(message => message.Id == id)
            ?? throw new InvalidOperationException(
                $"Parked message {id} of '{subscriptionId}' is not listed: it was replayed or dismissed, or it doesn't exist.");
    }

    /// <summary>
    /// Replays <paramref name="message"/>, and records a success in its parked stream.
    /// </summary>
    async ValueTask<ParkedMessageReplay> ReplayAsync(ParkedMessage message, CancellationToken cancellationToken) {
        Exception? error;
        try {
            error = await ReplayCoreAsync(message, cancellationToken);
        } catch (Exception exception) when (!cancellationToken.IsCancellationRequested) {
            error = exception;
        }

        if (error is not null) {
            LogReplayFailed(logger, error, message.Id, message.SubscriptionId, message.Source);
            return new ParkedMessageReplay(message, error);
        }

        await Store.AppendParkedAsync(message.SubscriptionId, ParkedFormat.ReplayedEventType, ParkedFormat.WriteClosed(message), cancellationToken);
        LogReplayed(logger, message.Id, message.SubscriptionId, message.Source);
        return new ParkedMessageReplay(message, null);
    }

    /// <summary>
    /// Reads the event or request of <paramref name="message"/> back and hands it to its handler.
    /// </summary>
    /// <returns>The exception of the last attempt, or <see langword="null"/> when the handler succeeded.</returns>
    async ValueTask<Exception?> ReplayCoreAsync(ParkedMessage message, CancellationToken cancellationToken) {
        if (message.Source is not { } source)
            throw new InvalidOperationException(
                $"Parked message {message.Id} of '{message.SubscriptionId}' doesn't tell which event it refers to: it can't be replayed, only dismissed.");
        if (await Store.ReadEventAsync(source, cancellationToken) is not { } entry)
            throw new InvalidOperationException(
                $"Event {source} of parked message {message.Id} of '{message.SubscriptionId}' doesn't exist anymore: it can't be replayed, only dismissed.");

        return entry.EventType is ScheduledFormat.CommandRequestEventType or ScheduledFormat.DeadlineRequestEventType
            ? await ReplayRequestAsync(message, source, entry, cancellationToken)
            : await ReplayEventAsync(message, source, entry, cancellationToken);
    }

    /// <summary>
    /// Hands the event of <paramref name="message"/> to the subscription that handles its id.
    /// </summary>
    async ValueTask<Exception?> ReplayEventAsync(ParkedMessage message, EventReference source, StoredEntry entry, CancellationToken cancellationToken) {
        var subscription = FindSubscription(message.SubscriptionId, scheduler: false);
        var @event = Deserialize(entry.EventType, entry.Data, source);
        if (!subscription.Registration.EventType.IsInstanceOfType(@event))
            throw new InvalidOperationException(
                $"Event {source} is a {@event.GetType().Name}, which subscription '{subscription.Registration.SubscriptionId}' doesn't handle.");

        var replayed = new SubscriptionMessage(@event, message.CommitPosition, source, entry.Metadata);
        return await retryPolicy.TryExecuteAsync(ct => subscription.HandleAsync(replayed, ct), cancellationToken);
    }

    /// <summary>
    /// Dispatches the deferred command of <paramref name="message"/>, or delivers its saga deadline to
    /// the saga whose scheduler parked it.
    /// </summary>
    async ValueTask<Exception?> ReplayRequestAsync(ParkedMessage message, EventReference source, StoredEntry entry, CancellationToken cancellationToken) {
        if (!ScheduledFormat.TryReadRequest(entry.Data, out var envelope))
            throw new InvalidOperationException($"Request {source} of parked message {message.Id} of '{message.SubscriptionId}' is not valid.");
        var payload = Deserialize(envelope.Type, envelope.Data, source);

        if (entry.EventType == ScheduledFormat.DeadlineRequestEventType) {
            var subscription = FindSubscription(message.SubscriptionId, scheduler: true);
            var deadline = new ScheduledDeadline(source, envelope.Due, payload, entry.Metadata, message.CommitPosition);
            return await retryPolicy.TryExecuteAsync(ct => subscription.HandleDeadlineAsync(deadline, ct), cancellationToken);
        }

        if (payload is not ICommand command)
            throw new InvalidOperationException($"Request {source} holds a {payload.GetType().Name}, which is not a command.");
        return await retryPolicy.TryExecuteAsync(async ct => {
            await using var scope = scopeFactory.CreateAsyncScope();
            // The metadata of the request, so that the command carries the correlation of the event that deferred it.
            await using var metadataScope = new MetadataScope(entry.Metadata);
            await scope.ServiceProvider.GetRequiredService<ICommandDispatcher>().DispatchAsync(command, ct);
        }, cancellationToken);
    }

    /// <summary>
    /// Deserializes stored data; a failure, or a type the application doesn't know, fails the replay.
    /// </summary>
    object Deserialize(string type, ReadOnlyMemory<byte> data, EventReference source) {
        object? value;
        try {
            value = Store.Deserialize(type, data);
        } catch (Exception exception) {
            throw new InvalidOperationException($"{source} can't be deserialized.", exception);
        }
        return value ?? throw new InvalidOperationException($"{source} can't be deserialized: Deserialize doesn't know the type '{type}'.");
    }

    /// <summary>
    /// The subscription of this host that handles the messages parked under <paramref name="subscriptionId"/>:
    /// the subscription with that id, or the one that continues from it; for a scheduler, the
    /// subscription whose scheduler it is, or the one that continues from that subscription.
    /// </summary>
    IReplayableSubscription FindSubscription(string subscriptionId, bool scheduler) {
        IReplayableSubscription[] found = [.. subscriptions.Where(subscription => Handles(subscription.Registration, subscriptionId, scheduler))];
        return found switch {
            [var subscription] => subscription,
            [] => throw new InvalidOperationException(
                $"No subscription in this host handles the messages parked under '{subscriptionId}': " +
                "replay them in a host that registers its handler, or a newer version of it."),
            _ => throw new InvalidOperationException(
                $"The messages parked under '{subscriptionId}' are handled by {found.Length} subscriptions in this host " +
                $"({string.Join(", ", found.Select(subscription => subscription.Registration.SubscriptionId))}): " +
                "replay them in a host that registers only one of them."),
        };
    }

    static bool Handles(SubscriptionRegistration registration, string subscriptionId, bool scheduler) =>
        new[] { registration.SubscriptionId }.Concat(registration.Predecessors ?? [])
            .Any(id => string.Equals(scheduler ? ScheduledFormat.SchedulerStream(id) : id, subscriptionId, StringComparison.Ordinal));

    [LoggerMessage(Level = LogLevel.Information, Message = "Replayed parked message {Id} of '{SubscriptionId}' ({Source})")]
    static partial void LogReplayed(ILogger logger, string id, string subscriptionId, EventReference? source);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Replaying parked message {Id} of '{SubscriptionId}' ({Source}) failed; it stays parked")]
    static partial void LogReplayFailed(ILogger logger, Exception exception, string id, string subscriptionId, EventReference? source);

    [LoggerMessage(Level = LogLevel.Information, Message = "Dismissed parked message {Id} of '{SubscriptionId}' ({Source})")]
    static partial void LogDismissed(ILogger logger, string id, string subscriptionId, EventReference? source);
}
