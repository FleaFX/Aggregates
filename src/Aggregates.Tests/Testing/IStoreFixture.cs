using Aggregates.Policies;
using Aggregates.Projections;
using Aggregates.Sagas;

namespace Aggregates.Testing;

/// <summary>
/// The event store transports the integration tests run against.
/// </summary>
public enum Transport {
    /// <summary>
    /// KurrentDB in a container.
    /// </summary>
    KurrentDB,

    /// <summary>
    /// Embedded MSSP in a temporary directory.
    /// </summary>
    MSSP
}

/// <summary>
/// An event read back from the store as it was stored, without deserialization.
/// </summary>
/// <param name="Stream">The stream the event belongs to.</param>
/// <param name="EventType">The stored event type name.</param>
/// <param name="Position">The global position of the event.</param>
/// <param name="Data">The stored payload.</param>
/// <param name="Metadata">The stored metadata; empty when none was written.</param>
sealed record StoredEvent(string Stream, string EventType, ulong Position, ReadOnlyMemory<byte> Data, ReadOnlyMemory<byte> Metadata);

/// <summary>
/// A fresh event store for a single test, and the transport-specific wiring of the Aggregates
/// integration packages against it. The fixture owns the store, so it outlives the test hosts
/// that use it (for example, to restart a host on the same store).
/// </summary>
interface IStoreFixture : IAsyncDisposable {
    /// <summary>
    /// The transport of this store.
    /// </summary>
    Transport Transport { get; }

    /// <summary>
    /// Registers the store client and the transport's storage integration.
    /// </summary>
    void ConfigureAggregates(IAggregatesBuilder builder, SerializationSetup serialization);

    /// <summary>
    /// Registers the transport's subscription infrastructure for projections.
    /// </summary>
    void ConfigureProjections(IProjectionsBuilder builder);

    /// <summary>
    /// Registers the transport's subscription infrastructure for policies.
    /// </summary>
    void ConfigurePolicies(IPoliciesBuilder builder);

    /// <summary>
    /// Registers the transport's saga storage and subscription infrastructure.
    /// </summary>
    void ConfigureSagas(ISagasBuilder builder);

    /// <summary>
    /// Appends a single event as-is, bypassing the Aggregates serialization.
    /// </summary>
    ValueTask AppendRawAsync(string stream, string eventType, ReadOnlyMemory<byte> data, ReadOnlyMemory<byte> metadata = default);

    /// <summary>
    /// Reads every event in global order, without deserialization. Only the store's own
    /// system streams (KurrentDB: streams starting with <c>$</c>) are left out; events that
    /// Aggregates writes itself, such as checkpoints, are included.
    /// </summary>
    ValueTask<IReadOnlyList<StoredEvent>> ReadAllAsync();

    /// <summary>
    /// Makes the store unreachable without losing its data, until <see cref="ResumeAsync"/>.
    /// </summary>
    ValueTask InterruptAsync();

    /// <summary>
    /// Makes the store reachable again after <see cref="InterruptAsync"/>.
    /// </summary>
    ValueTask ResumeAsync();
}
