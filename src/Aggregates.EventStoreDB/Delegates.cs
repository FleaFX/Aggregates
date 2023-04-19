using EventStore.Client;

namespace Aggregates.EventStoreDB;

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
/// Asynchronously subscribes to a persistent subscription to $all.
/// </summary>
delegate Task<PersistentSubscription> SubscribeToAllAsync(string groupName,
    Func<PersistentSubscription, ResolvedEvent, int?, CancellationToken, Task> eventAppeared,
    Action<PersistentSubscription, SubscriptionDroppedReason, Exception?>? subscriptionDropped = null,
    UserCredentials? credentials = null, int bufferSize = 10,
    CancellationToken cancellationToken = default);