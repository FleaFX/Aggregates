using Aggregates.Subscriptions;
using KurrentDB.Client;

namespace Aggregates.KurrentDB;

/// <summary>
/// Creates KurrentDB <c>$all</c> subscriptions. Deserialization is handled via
/// <see cref="KurrentDbOptions.Deserialize"/>. System events are excluded via
/// <see cref="EventTypeFilter.ExcludeSystemEvents"/>.
/// </summary>
public sealed class KurrentDbSubscriptionFactory(KurrentDBClient client, KurrentDbOptions options) : ISubscriptionFactory {
    /// <inheritdoc/>
    public ISubscription Subscribe(ulong? fromPosition, bool startFromEnd, CancellationToken cancellationToken = default) {
        var from = (fromPosition, startFromEnd) switch {
            ({ } pos, _) => FromAll.After(new Position(pos, pos)),
            (null, true) => FromAll.End,
            _ => FromAll.Start,
        };

        var filterOptions = new SubscriptionFilterOptions(EventTypeFilter.ExcludeSystemEvents());
        var subscription = client.SubscribeToAll(from, filterOptions: filterOptions, cancellationToken: cancellationToken);
        return new KurrentDbSubscription(() => subscription.DisposeAsync(), subscription.Messages, options);
    }
}
