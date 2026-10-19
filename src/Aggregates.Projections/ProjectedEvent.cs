namespace Aggregates.Projections;

/// <summary>
/// The live event a projection subscription is projecting, available to the projection and its
/// commit through <see cref="Current"/>.
/// </summary>
/// <remarks>
/// Lets a commit record the event's position with its writes, so the event has no effect twice
/// when it is delivered again after a crash. <see cref="Current"/> is <see langword="null"/> when
/// a parked event is replayed (it lies before positions already recorded) and outside a
/// projection subscription.
/// </remarks>
public sealed class ProjectedEvent {
    static readonly AsyncLocal<ProjectedEvent?> CurrentEvent = new();

    int _transactions;

    /// <summary>
    /// Initializes a new <see cref="ProjectedEvent"/>.
    /// </summary>
    /// <param name="subscriptionId">The id of the subscription projecting the event.</param>
    /// <param name="position">The commit position of the event.</param>
    /// <param name="services">The services of the scope the event is projected in.</param>
    internal ProjectedEvent(string subscriptionId, ulong position, IServiceProvider services) {
        SubscriptionId = subscriptionId;
        Position = position;
        Services = services;
    }

    /// <summary>
    /// The live event being projected in the current asynchronous flow, or <see langword="null"/>.
    /// </summary>
    public static ProjectedEvent? Current {
        get => CurrentEvent.Value;
        internal set => CurrentEvent.Value = value;
    }

    /// <summary>
    /// The id of the subscription projecting the event.
    /// </summary>
    public string SubscriptionId { get; }

    /// <summary>
    /// The commit position of the event.
    /// </summary>
    public ulong Position { get; }

    /// <summary>
    /// The services of the scope the event is projected in.
    /// </summary>
    public IServiceProvider Services { get; }

    /// <summary>
    /// Numbers the transactions that record this event's position, from 1, so each of several
    /// transactions for the event is recognized on its own when the event is delivered again.
    /// Every attempt to project the event starts again from 1.
    /// </summary>
    public int NextTransaction() => ++_transactions;
}
