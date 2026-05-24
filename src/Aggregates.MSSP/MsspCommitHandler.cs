using MSSP;

namespace Aggregates.MSSP;

/// <summary>
/// Persists aggregate changes to MSSP. Appends uncommitted events from the ambient
/// <see cref="UnitOfWork"/> to the aggregate's stream, using optimistic concurrency control.
/// </summary>
public sealed class MsspCommitHandler(IMsspClient client, MsspOptions options) {

    /// <summary>
    /// Appends the single changed aggregate's events to its MSSP stream.
    /// </summary>
    public async ValueTask CommitAsync(UnitOfWork unitOfWork) {
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
                let serialized = options.Serialize!(change)
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
