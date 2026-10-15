using Aggregates.KurrentDB;
using Docker.DotNet.Models;
using Grpc.Core;
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
    static readonly TimeSpan ReadyTimeout = TimeSpan.FromSeconds(60);
    static readonly TimeSpan ReadyPollInterval = TimeSpan.FromMilliseconds(250);
    readonly KurrentDbContainer _container;
    readonly KurrentDBClient _client;

    KurrentDbStoreFixture(KurrentDbContainer container, KurrentDBClient client) {
        _container = container;
        _client = client;
    }

    /// <summary>
    /// Starts a new container and waits until it is healthy and its node serves requests.
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
        await WaitUntilReadyAsync(container.GetConnectionString(), client, TestContext.Current.CancellationToken);
        return new KurrentDbStoreFixture(container, client);
    }

    /// <summary>
    /// Waits until the node serves a read and a persistent subscriptions request. The image's
    /// health check only tells that the process is alive: until the node is leader, requests fail
    /// with "Server Is Not Ready".
    /// </summary>
    static async Task WaitUntilReadyAsync(string connectionString, KurrentDBClient client, CancellationToken cancellationToken) {
        await using var persistentSubscriptions = new KurrentDBPersistentSubscriptionsClient(KurrentDBClientSettings.Create(connectionString));
        var deadline = DateTime.UtcNow + ReadyTimeout;
        while (true) {
            try {
                await client.ReadAllAsync(Direction.Forwards, Position.Start, maxCount: 1, cancellationToken: cancellationToken)
                    .ToArrayAsync(cancellationToken);
                await persistentSubscriptions.ListAllAsync(cancellationToken: cancellationToken);
                return;
            } catch (Exception exception) when (IsNotReady(exception) && DateTime.UtcNow < deadline) {
                await Task.Delay(ReadyPollInterval, cancellationToken);
            }
        }
    }

    /// <summary>
    /// Whether <paramref name="exception"/> means that the node doesn't serve requests yet.
    /// </summary>
    static bool IsNotReady(Exception exception) =>
        exception is NotLeaderException or RpcException { StatusCode: StatusCode.Unavailable or StatusCode.DeadlineExceeded };

    /// <inheritdoc/>
    public Transport Transport => Transport.KurrentDB;

    /// <summary>
    /// The client of the container, for tests that use KurrentDB directly.
    /// </summary>
    public KurrentDBClient Client => _client;

    /// <summary>
    /// The connection string of the container, for clients other than <see cref="Client"/>.
    /// </summary>
    public string ConnectionString => _container.GetConnectionString();

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
