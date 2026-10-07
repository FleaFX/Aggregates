using Aggregates.KurrentDB;
using Docker.DotNet.Models;
using Aggregates.Policies;
using Aggregates.Policies.KurrentDB;
using Aggregates.Projections;
using Aggregates.Projections.KurrentDB;
using Aggregates.Sagas;
using Aggregates.Sagas.KurrentDB;
using KurrentDB.Client;
using Microsoft.Extensions.DependencyInjection;
using Testcontainers.KurrentDb;
// Aggregates.KurrentDB has an internal KurrentDbBuilder too (visible to this assembly).
using KurrentDbContainerBuilder = Testcontainers.KurrentDb.KurrentDbBuilder;

namespace Aggregates.Testing;

/// <summary>
/// A fresh single-node KurrentDB container, in memory and without TLS.
/// </summary>
sealed class KurrentDbStoreFixture : IStoreFixture {
    readonly KurrentDbContainer _container;
    readonly KurrentDBClient _client;

    KurrentDbStoreFixture(KurrentDbContainer container, KurrentDBClient client) {
        _container = container;
        _client = client;
    }

    /// <summary>
    /// Starts a new container and waits until it is healthy.
    /// </summary>
    public static async Task<KurrentDbStoreFixture> StartAsync() {
        var container = new KurrentDbContainerBuilder(TestImages.KurrentDb)
            .WithEnvironment("KURRENTDB_MEM_DB", "true")
            .WithEnvironment("KURRENTDB_RUN_PROJECTIONS", "None")
            .WithEnvironment("KURRENTDB_START_STANDARD_PROJECTIONS", "false")
            // The image checks health every 5 s, which dominates the start-up time. Probe faster during
            // a start period instead (the check itself is inherited); failures in that period do not
            // count towards the failing streak that makes Testcontainers give up.
            .WithCreateParameterModifier(p => p.Healthcheck = new HealthcheckConfig { StartPeriod = TimeSpan.FromSeconds(60), StartInterval = TimeSpan.FromMilliseconds(250) })
            .Build();
        await container.StartAsync(TestContext.Current.CancellationToken);

        var client = new KurrentDBClient(KurrentDBClientSettings.Create(container.GetConnectionString()));
        return new KurrentDbStoreFixture(container, client);
    }

    /// <inheritdoc/>
    public Transport Transport => Transport.KurrentDB;

    /// <inheritdoc/>
    public void ConfigureAggregates(IAggregatesBuilder builder, SerializationSetup serialization) {
        builder.Services.AddSingleton(_client);
        builder.AddKurrentDb(o => {
            o.Serialize = e => new SerializedEvent(serialization.TypeName(e.GetType()), serialization.SerializeData(e));
            o.Deserialize = serialization.Deserialize;
            o.SerializeMetadata = serialization.SerializeMetadata;
            o.DeserializeMetadata = serialization.DeserializeMetadata;
        });
    }

    /// <inheritdoc/>
    public void ConfigureProjections(IProjectionsBuilder builder) => builder.AddKurrentDb();

    /// <inheritdoc/>
    public void ConfigurePolicies(IPoliciesBuilder builder) => builder.AddKurrentDb();

    /// <inheritdoc/>
    public void ConfigureSagas(ISagasBuilder builder) => builder.AddKurrentDb();

    /// <inheritdoc/>
    public async ValueTask AppendRawAsync(string stream, string eventType, ReadOnlyMemory<byte> data, ReadOnlyMemory<byte> metadata = default) =>
        await _client.AppendToStreamAsync(stream, StreamState.Any, [new EventData(Uuid.NewUuid(), eventType, data, metadata)]);

    /// <inheritdoc/>
    public async ValueTask<IReadOnlyList<StoredEvent>> ReadAllAsync() {
        var events = new List<StoredEvent>();
        await foreach (var resolved in _client.ReadAllAsync(Direction.Forwards, Position.Start)) {
            var record = resolved.Event;
            if (record.EventStreamId.StartsWith('$'))
                continue;
            events.Add(new StoredEvent(record.EventStreamId, record.EventType, record.Position.CommitPosition, record.Data, record.Metadata));
        }
        return events;
    }

    /// <inheritdoc/>
    public async ValueTask InterruptAsync() => await _container.PauseAsync();

    /// <inheritdoc/>
    public async ValueTask ResumeAsync() => await _container.UnpauseAsync();

    /// <inheritdoc/>
    public async ValueTask DisposeAsync() {
        await _client.DisposeAsync();
        await _container.DisposeAsync();
    }
}
