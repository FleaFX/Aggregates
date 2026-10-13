using MSSP;

namespace Aggregates.MSSP;

/// <summary>
/// Persists aggregate changes to MSSP. Appends uncommitted events from the ambient
/// <see cref="UnitOfWork"/> to the aggregate's stream, using optimistic concurrency control.
/// </summary>
public sealed class MsspCommitHandler(IMsspClient client, MsspOptions options) {

    /// <summary>
    /// Appends the single changed aggregate's events to its MSSP stream, serialized with
    /// <see cref="MsspOptions.Serialize"/>.
    /// </summary>
    /// <param name="unitOfWork">The unit of work holding the aggregate to commit.</param>
    public ValueTask CommitAsync(UnitOfWork unitOfWork) => CommitAsync(unitOfWork, options.Serialize!);

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
