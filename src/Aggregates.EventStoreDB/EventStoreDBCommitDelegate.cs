using EventStore.Client;

namespace Aggregates.EventStoreDB;

static class EventStoreDbCommitDelegate {
    /// <summary>
    /// Creates a <see cref="EntityCommitDelegate"/> that commits changes to the given <paramref name="client"/>.
    /// </summary>
    /// <param name="client">The <see cref="EventStoreClient"/>.</param>
    /// <param name="serializer">Creates a <see cref="EventData"/> for each change.</param>
    /// <returns>A <see cref="EntityCommitDelegate"/>.</returns>
    public static EntityCommitDelegate CreateEntityDelegate(EventStoreClient client, Func<Aggregate, object, EventData> serializer) =>
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

    /// <summary>
    /// Creates a <see cref="SagaCommitDelegate"/> that commits changes to the given <paramref name="client"/>.
    /// </summary>
    /// <param name="client">The <see cref="EventStoreClient"/>.</param>
    /// <param name="serializer">Creates a <see cref="EventData"/> for each change.</param>
    /// <returns>A <see cref="SagaCommitDelegate"/>.</returns>
    public static SagaCommitDelegate CreateSagaDelegate(EventStoreClient client, Func<Aggregate, object, EventData> serializer) =>
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