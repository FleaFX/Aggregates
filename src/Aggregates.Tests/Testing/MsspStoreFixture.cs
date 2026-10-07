using Aggregates.MSSP;
using Aggregates.Policies;
using Aggregates.Policies.MSSP;
using Aggregates.Projections;
using Aggregates.Projections.MSSP;
using Aggregates.Sagas;
using Aggregates.Sagas.MSSP;
using Microsoft.Extensions.DependencyInjection;
// Inside namespace Aggregates.*, "MSSP" binds to Aggregates.MSSP, hence the aliases.
using EmbeddedMsspClient = global::MSSP.Engine.EmbeddedMsspClient;
using IMsspClient = global::MSSP.IMsspClient;
using MsspEventData = global::MSSP.EventData;
using StreamRevision = global::MSSP.StreamRevision;
using SubscriptionFilter = global::MSSP.SubscriptionFilter;
using GlobalPosition = global::MSSP.GlobalPosition;

namespace Aggregates.Testing;

/// <summary>
/// A fresh embedded MSSP store in its own temporary directory, deleted on dispose.
/// </summary>
/// <remarks>
/// The store is opened here rather than with MSSP's <c>services.AddMssp(...)</c>: that
/// registration only provides a client once its hosted service has started, while the generic
/// host constructs every hosted service, including the Aggregates subscription services that
/// need the client, before starting any of them.
/// </remarks>
sealed class MsspStoreFixture : IStoreFixture {
    readonly string _directory;
    readonly EmbeddedMsspClient _client;

    MsspStoreFixture(string directory, EmbeddedMsspClient client) {
        _directory = directory;
        _client = client;
    }

    /// <summary>
    /// Opens a new, empty store.
    /// </summary>
    public static async Task<MsspStoreFixture> StartAsync() {
        var directory = Path.Combine(Path.GetTempPath(), "aggregates-tests", Guid.NewGuid().ToString("N"));
        var client = await EmbeddedMsspClient.OpenAsync(directory, cancellationToken: TestContext.Current.CancellationToken);
        return new MsspStoreFixture(directory, client);
    }

    /// <inheritdoc/>
    public Transport Transport => Transport.MSSP;

    /// <inheritdoc/>
    public void ConfigureAggregates(IAggregatesBuilder builder, SerializationSetup serialization) {
        builder.Services.AddSingleton<IMsspClient>(_client);
        builder.AddMssp(o => {
            o.Serialize = e => new MsspEventData(serialization.TypeName(e.GetType()), serialization.SerializeData(e));
            o.Deserialize = serialization.Deserialize;
            o.SerializeMetadata = serialization.SerializeMetadata;
            o.DeserializeMetadata = serialization.DeserializeMetadata;
        });
    }

    /// <inheritdoc/>
    public void ConfigureProjections(IProjectionsBuilder builder) => builder.AddMssp();

    /// <inheritdoc/>
    public void ConfigurePolicies(IPoliciesBuilder builder) => builder.AddMssp();

    /// <inheritdoc/>
    public void ConfigureSagas(ISagasBuilder builder) => builder.AddMssp();

    /// <inheritdoc/>
    public ValueTask AppendRawAsync(string stream, string eventType, ReadOnlyMemory<byte> data, ReadOnlyMemory<byte> metadata = default) =>
        _client.AppendAsync(stream, StreamRevision.Any, [new MsspEventData(eventType, data, metadata)]);

    /// <inheritdoc/>
    public async ValueTask<IReadOnlyList<StoredEvent>> ReadAllAsync() {
        // MSSP has no read-all; a catch-up subscription up to the current position does the same.
        var events = new List<StoredEvent>();
        var end = _client.CurrentPosition;
        if (end == GlobalPosition.Start)
            return events;

        // The timeout turns a wrong assumption about CurrentPosition into a failure instead of a hang.
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(TestContext.Current.CancellationToken);
        timeout.CancelAfter(Eventually.DefaultTimeout);
        await foreach (var e in _client.SubscribeAsync(SubscriptionFilter.All, GlobalPosition.Start, timeout.Token)) {
            events.Add(new StoredEvent(e.StreamId.Value, e.EventType, e.Position.Value, e.Data, e.Metadata));
            if (e.Position >= end)
                break;
        }
        return events;
    }

    /// <inheritdoc/>
    public ValueTask InterruptAsync() =>
        throw new NotSupportedException("Interrupting an embedded MSSP store is not supported.");

    /// <inheritdoc/>
    public ValueTask ResumeAsync() =>
        throw new NotSupportedException("Interrupting an embedded MSSP store is not supported.");

    /// <inheritdoc/>
    public async ValueTask DisposeAsync() {
        await _client.DisposeAsync();
        try {
            Directory.Delete(_directory, recursive: true);
        } catch (IOException) {
            // Best effort: a leftover temp directory must not fail the test.
        }
    }
}
