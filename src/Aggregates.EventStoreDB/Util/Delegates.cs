using EventStore.Client;

namespace Aggregates.EventStoreDB.Util;

/// <summary>
/// Asynchronously creates a filtered persistent subscription to $all.
/// </summary>
delegate Task CreateToAllAsyncDelegate(string groupName, IEventFilter eventFilter,
    PersistentSubscriptionSettings settings, TimeSpan? deadline = null, UserCredentials? credentials = null, CancellationToken cancellationToken = default);

/// <summary>
/// Asynchronously lists persistent subscriptions to $all.
/// </summary>
delegate Task<IEnumerable<PersistentSubscriptionInfo>> ListToAllAsyncDelegate(TimeSpan? deadline = null, UserCredentials? credentials = null, CancellationToken cancellationToken = default);

/// <summary>
/// Subscribes to a persistent subscription to $all.
/// </summary>
delegate EventStorePersistentSubscriptionsClient.PersistentSubscriptionResult SubscribeToAll(
    string groupName,
    int bufferSize = 10,
    UserCredentials? userCredentials = null,
    CancellationToken cancellationToken = default (CancellationToken));