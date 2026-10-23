using System.Buffers.Binary;
using System.IO.Hashing;
using System.Security.Cryptography;
using System.Text;
using Aggregates.Testing;
using FluentAssertions;
using KurrentDB.Client;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Testing;

namespace Aggregates.KurrentDB;

public class KurrentDbCommitHandlerTests {
    // Helper: a serialized event with sensible defaults.
    static SerializedEvent Event(string type = "OrderPlaced", byte[]? data = null) =>
        new(type, data ?? [1, 2, 3]);

    [Fact]
    public void CreateEventId_GivenSameInputs_ReturnsSameUuid() {
        var id1 = KurrentDbCommitHandler.CreateEventId("orders-1", 0, Event());
        var id2 = KurrentDbCommitHandler.CreateEventId("orders-1", 0, Event());

        id1.Should().Be(id2);
    }

    [Fact]
    public void CreateEventId_GivenDifferentStreamName_ReturnsDifferentUuid() {
        var id1 = KurrentDbCommitHandler.CreateEventId("orders-1", 0, Event());
        var id2 = KurrentDbCommitHandler.CreateEventId("orders-2", 0, Event());

        id1.Should().NotBe(id2);
    }

    [Fact]
    public void CreateEventId_GivenDifferentPosition_ReturnsDifferentUuid() {
        var id1 = KurrentDbCommitHandler.CreateEventId("orders-1", 0, Event());
        var id2 = KurrentDbCommitHandler.CreateEventId("orders-1", 1, Event());

        id1.Should().NotBe(id2);
    }

    [Fact]
    public void CreateEventId_GivenDifferentPayload_ReturnsDifferentUuid() {
        var id1 = KurrentDbCommitHandler.CreateEventId("orders-1", 0, Event(data: [1, 2, 3]));
        var id2 = KurrentDbCommitHandler.CreateEventId("orders-1", 0, Event(data: [4, 5, 6]));

        id1.Should().NotBe(id2);
    }

    [Fact]
    public void CreateEventId_GivenDifferentEventType_ReturnsDifferentUuid() {
        var id1 = KurrentDbCommitHandler.CreateEventId("orders-1", 0, Event(type: "OrderPlaced"));
        var id2 = KurrentDbCommitHandler.CreateEventId("orders-1", 0, Event(type: "OrderCancelled"));

        id1.Should().NotBe(id2);
    }

    [Fact]
    public void CreateEventId_GivenALongStreamName_IsDeterministic() {
        var stream = new string('x', 600);
        var id1 = KurrentDbCommitHandler.CreateEventId(stream, 0, Event());
        var id2 = KurrentDbCommitHandler.CreateEventId(stream, 0, Event());

        id1.Should().Be(id2);
    }

    [Fact]
    public void CreateEventId_HashesTheConcatenatedFields() {
        foreach (var stream in new[] { "orders-1", new string('x', 600) }) {
            var type = "OrderPlaced";
            var data = new byte[] { 1, 2, 3 };
            var expectedBytes = new byte[Encoding.UTF8.GetByteCount(stream) + sizeof(long) + sizeof(ulong) + Encoding.UTF8.GetByteCount(type)];
            var written = 0;
            written += Encoding.UTF8.GetBytes(stream, expectedBytes.AsSpan(written));
            BinaryPrimitives.WriteInt64LittleEndian(expectedBytes.AsSpan(written), 5);
            written += sizeof(long);
            BinaryPrimitives.WriteUInt64LittleEndian(expectedBytes.AsSpan(written), XxHash64.HashToUInt64(data));
            written += sizeof(ulong);
            written += Encoding.UTF8.GetBytes(type, expectedBytes.AsSpan(written));

            var id = KurrentDbCommitHandler.CreateEventId(stream, 5, Event(type: type, data: data));

            id.Should().Be(Uuid.FromGuid(new Guid(MD5.HashData(expectedBytes))));
        }
    }

    // The commit handler serializes lazily while appending, so these tests need a reachable store.
    [Trait("Category", "Integration")]
    public class TypeNameCheck(ITestOutputHelper output) {
        static KurrentDbOptions WrongNamingOptions { get; } = new() { Serialize = _ => new SerializedEvent("Wrong.Name@v1", new byte[] { 1 }) };

        static async Task<UnitOfWork> ChangedUnitOfWork(string stream) {
            var unitOfWork = new UnitOfWork();
            var root = new EntityRoot<OrderState, IOrderEvent>(OrderState.Initial, AggregateVersion.None);
            unitOfWork.Attach(new Aggregate(stream, root));
            await root.AcceptAsync(new PlaceOrder(stream, "alice"), TestContext.Current.CancellationToken);
            return unitOfWork;
        }

        static string? State(FakeLogRecord record, string key) =>
            record.StructuredState!.Single(entry => entry.Key == key).Value;

        [Fact]
        public async Task GivenANameTheRegistryDoesNotKnow_WarnsOncePerType() {
            await using var store = await Stores.StartAsync(Transport.KurrentDB);
            await using var host = await TestHost.StartAsync(store, output, o => o.Events(Orders.EventTypes).Projections());
            var logger = new FakeLogger<KurrentDbCommitHandler>();
            var handler = new KurrentDbCommitHandler(
                host.Services.GetRequiredService<KurrentDBClient>(), WrongNamingOptions, logger,
                EventTypeRegistry.FromTypes([typeof(OrderPlaced)]));

            await handler.CommitAsync(await ChangedUnitOfWork("orders-1"));
            await handler.CommitAsync(await ChangedUnitOfWork("orders-2"));

            var warning = logger.Collector.GetSnapshot().Single(record => record.Level == LogLevel.Warning && record.Message.Contains("names it"));
            State(warning, "Type").Should().Be(typeof(OrderPlaced).FullName);
            State(warning, "StoredName").Should().Be("Wrong.Name@v1");
            State(warning, "RegistryName").Should().Be("IntegrationTests.OrderPlaced@v1");
        }

        [Fact]
        public async Task GivenAnEventTypeOutsideTheRegistry_WarnsOnceThatItCanNotBeReadBack() {
            await using var store = await Stores.StartAsync(Transport.KurrentDB);
            await using var host = await TestHost.StartAsync(store, output, o => o.Events(Orders.EventTypes).Projections());
            var logger = new FakeLogger<KurrentDbCommitHandler>();
            var registry = EventTypeRegistry.FromTypes([typeof(Shipped)]);
            var options = new KurrentDbOptions { Serialize = @event => new SerializedEvent(registry.GetTypeName(@event.GetType()), new byte[] { 1 }) };
            var handler = new KurrentDbCommitHandler(host.Services.GetRequiredService<KurrentDBClient>(), options, logger, registry);

            await handler.CommitAsync(await ChangedUnitOfWork("orders-3"));
            await handler.CommitAsync(await ChangedUnitOfWork("orders-4"));

            var warning = logger.Collector.GetSnapshot().Single(record => record.Level == LogLevel.Warning && record.Message.Contains("can't be read back"));
            State(warning, "Type").Should().Be(typeof(OrderPlaced).FullName);
            State(warning, "StoredName").Should().Be("IntegrationTests.OrderPlaced@v1");
        }

        [Fact]
        public async Task GivenTheRegistrysName_DoesNotWarn() {
            await using var store = await Stores.StartAsync(Transport.KurrentDB);
            await using var host = await TestHost.StartAsync(store, output, o => o.Events(Orders.EventTypes).Projections());
            var logger = new FakeLogger<KurrentDbCommitHandler>();
            var registry = EventTypeRegistry.FromTypes([typeof(OrderPlaced)]);
            var options = new KurrentDbOptions { Serialize = @event => new SerializedEvent(registry.GetTypeName(@event.GetType()), new byte[] { 1 }) };
            var handler = new KurrentDbCommitHandler(host.Services.GetRequiredService<KurrentDBClient>(), options, logger, registry);

            await handler.CommitAsync(await ChangedUnitOfWork("orders-1"));

            logger.Collector.GetSnapshot().Should().NotContain(record => record.Level == LogLevel.Warning);
        }

        [Fact]
        public async Task WithoutRegistry_DoesNotWarn() {
            await using var store = await Stores.StartAsync(Transport.KurrentDB);
            await using var host = await TestHost.StartAsync(store, output, o => o.Events(Orders.EventTypes).Projections());
            var logger = new FakeLogger<KurrentDbCommitHandler>();
            var handler = new KurrentDbCommitHandler(host.Services.GetRequiredService<KurrentDBClient>(), WrongNamingOptions, logger);

            await handler.CommitAsync(await ChangedUnitOfWork("orders-1"));

            logger.Collector.GetSnapshot().Should().NotContain(record => record.Level == LogLevel.Warning);
        }

        [Fact]
        public async Task GivenASagaCommit_DoesNotCheck() {
            await using var store = await Stores.StartAsync(Transport.KurrentDB);
            await using var host = await TestHost.StartAsync(store, output, o => o.Events(Orders.EventTypes).Projections());
            var logger = new FakeLogger<KurrentDbCommitHandler>();
            var options = new KurrentDbOptions { Serialize = _ => new SerializedEvent("IntegrationTests.OrderPlaced@v1", new byte[] { 1 }) };
            var handler = new KurrentDbCommitHandler(
                host.Services.GetRequiredService<KurrentDBClient>(), options, logger,
                EventTypeRegistry.FromTypes([typeof(OrderPlaced)]));

            await handler.CommitAsync(await ChangedUnitOfWork("orders-1"), _ => new SerializedEvent("Wrong.Name@v1", new byte[] { 1 }));

            logger.Collector.GetSnapshot().Should().NotContain(record => record.Level == LogLevel.Warning);
        }
    }
}
