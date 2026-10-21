using System.Collections.Concurrent;
using System.ComponentModel;
using Microsoft.Extensions.Logging;
using MSSP;

namespace Aggregates.MSSP.Internal;

/// <summary>
/// Persists aggregate changes to MSSP. Appends uncommitted events from the ambient
/// <see cref="UnitOfWork"/> to the aggregate's stream, using optimistic concurrency control.
/// </summary>
/// <remarks>
/// Internal infrastructure of the Aggregates packages. Public only so that those packages can use it;
/// it is not covered by semantic versioning and may change in any release.
/// </remarks>
[EditorBrowsable(EditorBrowsableState.Never)]
public sealed partial class MsspCommitHandler(
    IMsspClient client, MsspOptions options, ILogger<MsspCommitHandler> logger,
    EventTypeRegistry? eventTypeRegistry = null) {

    readonly ConcurrentDictionary<Type, byte> _checkedTypes = new();

    /// <summary>
    /// Appends the single changed aggregate's events to its MSSP stream, serialized with
    /// <see cref="MsspOptions.Serialize"/>.
    /// </summary>
    /// <param name="unitOfWork">The unit of work holding the aggregate to commit.</param>
    public ValueTask CommitAsync(UnitOfWork unitOfWork) =>
        CommitAsync(unitOfWork, eventTypeRegistry is null ? options.Serialize! : SerializeAndCheck);

    // Wraps Serialize to warn, once per CLR type per process, when the stored name differs from
    // the registry's: subscriptions filter on the registry's names and would miss the events.
    EventData SerializeAndCheck(object @event) {
        var serialized = options.Serialize!(@event);
        var type = @event.GetType();
        if (eventTypeRegistry is not null && _checkedTypes.TryAdd(type, 0)
            && !string.Equals(eventTypeRegistry.GetTypeName(type), serialized.EventType, StringComparison.Ordinal))
            LogTypeNameMismatch(logger, type.FullName, serialized.EventType, eventTypeRegistry.GetTypeName(type));
        return serialized;
    }

    [LoggerMessage(Level = LogLevel.Warning, Message = "Event type {Type} is stored as '{StoredName}', but the EventTypeRegistry names it '{RegistryName}'. Subscriptions filter on the registry's names and will not receive these events. Serialize with the registry's names, or remove the registry from the service collection to disable filtering.")]
    static partial void LogTypeNameMismatch(ILogger logger, string? type, string storedName, string registryName);

    /// <summary>
    /// Appends the single changed aggregate's events to its MSSP stream, serialized with
    /// <paramref name="serialize"/> instead of <see cref="MsspOptions.Serialize"/>. Only the
    /// event type and data of the result are stored; the metadata comes from
    /// <see cref="MsspOptions.SerializeMetadata"/> as usual. Used by saga commits, whose changes
    /// are references to source events rather than the events themselves.
    /// </summary>
    /// <param name="unitOfWork">The unit of work holding the aggregate to commit.</param>
    /// <param name="serialize">Serializes a change into the event to store.</param>
    public async ValueTask CommitAsync(UnitOfWork unitOfWork, Func<object, EventData> serialize) {
        if (unitOfWork.GetChanged() is not { } aggregate)
            return;

        var changes = aggregate.AggregateRoot.GetChanges().ToList();
        if (changes.Count == 0)
            return;

        var identifier = aggregate.Identifier;
        var version = (long)aggregate.AggregateRoot.Version;

        var metadata = MetadataScope.Current?.Snapshot() ?? EventMetadata.Empty;
        var serializedMetadata = options.SerializeMetadata?.Invoke(metadata) ?? default;

        try {
            await client.AppendAsync(
                identifier.ToString(),
                version < 0 ? StreamRevision.NoStream : (ulong)version,
                from change in changes
                let serialized = serialize(change)
                select new EventData(serialized.EventType, serialized.Data, serializedMetadata)
            );
        } catch (OptimisticConcurrencyException ex) {
            throw new ConcurrencyException(
                aggregate.Identifier,
                aggregate.AggregateRoot.Version,
                new AggregateVersion((long)ex.ExpectedRevision)
            );
        }
    }
}
