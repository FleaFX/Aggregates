using EventStore.Client;

namespace Aggregates.EventStoreDB; 

static class EventStoreDBCommitDelegate {
    /// <summary>
    /// Creates a <see cref="CommitDelegate"/> that commits changes to the given <paramref name="client"/>.
    /// </summary>
    /// <param name="client">The <see cref="EventStoreClient"/>.</param>
    /// <param name="serializer">Creates a <see cref="EventData"/> for each change.</param>
    /// <returns>A <see cref="CommitDelegate"/>.</returns>
    public static CommitDelegate Create(EventStoreClient client, Func<Aggregate, object, EventData> serializer) =>
        async unitOfWork => {
            var changed = unitOfWork.GetChanged();
            if (changed is { } aggregate) {
                await client.AppendToStreamAsync(
                    aggregate.Identifier.Value,
                    AggregateVersion.None.Equals(aggregate.AggregateRoot.Version) ? StreamRevision.None : StreamRevision.FromInt64(aggregate.AggregateRoot.Version),
                    aggregate.AggregateRoot.GetChanges().Select(@event => serializer(aggregate, @event))
                );
            }
        };
}