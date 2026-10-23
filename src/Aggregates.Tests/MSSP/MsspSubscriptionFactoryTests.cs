using System.Threading.Channels;
using Aggregates.Subscriptions;
using Aggregates.Testing;
using FakeItEasy;
using FluentAssertions;
using Grpc.Core;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Testing;
using IMsspClient = global::MSSP.IMsspClient;
using MsspEventData = global::MSSP.EventData;
using MsspStreamId = global::MSSP.StreamId;
using MsspStreamRevision = global::MSSP.StreamRevision;

namespace Aggregates.MSSP;

public class MsspSubscriptionFactoryTests {
    public class IsTransient : MsspSubscriptionFactoryTests {
        readonly MsspSubscriptionFactory _factory = new(A.Fake<IMsspClient>(), new MsspOptions());

        public static TheoryData<Exception> NotTransient => [
            new RpcException(new Status(StatusCode.PermissionDenied, "")),
            new RpcException(new Status(StatusCode.Unauthenticated, "")),
            new RpcException(new Status(StatusCode.InvalidArgument, "")),
            new RpcException(new Status(StatusCode.Unimplemented, "")),
            new RpcException(new Status(StatusCode.FailedPrecondition, "")),
        ];

        public static TheoryData<Exception> Transient => [
            new RpcException(new Status(StatusCode.Unavailable, "")),
            new RpcException(new Status(StatusCode.DeadlineExceeded, "")),
            new RpcException(new Status(StatusCode.Internal, "")),
            new TimeoutException("Could not determine the cluster leader within the timeout period."),
            new ObjectDisposedException("StoreEngine"),
            new ChannelClosedException(),
            new IOException(),
            new InvalidOperationException(),
        ];

        [Theory, MemberData(nameof(NotTransient))]
        public void GivenConfigurationFailure_ReturnsFalse(Exception exception) {
            _factory.IsTransient(exception).Should().BeFalse();
        }

        [Theory, MemberData(nameof(Transient))]
        public void GivenOtherFailure_ReturnsTrue(Exception exception) {
            _factory.IsTransient(exception).Should().BeTrue();
        }
    }

    public class CommitHandler {
        static IMsspClient NewClient() {
            var client = A.Fake<IMsspClient>();
            // The commit handler serializes lazily while appending, so the fake must consume the events.
            A.CallTo(() => client.AppendAsync(
                    A<MsspStreamId>._, A<MsspStreamRevision>._, A<IEnumerable<MsspEventData>>._, A<CancellationToken>._))
                .Invokes((MsspStreamId streamId, MsspStreamRevision revision, IEnumerable<MsspEventData> events, CancellationToken cancellationToken) => {
                    foreach (var @event in events) { }
                });
            return client;
        }

        static async Task<UnitOfWork> ChangedUnitOfWork() {
            var unitOfWork = new UnitOfWork();
            var root = new EntityRoot<OrderState, IOrderEvent>(OrderState.Initial, AggregateVersion.None);
            unitOfWork.Attach(new Aggregate("orders-1", root));
            await root.AcceptAsync(new PlaceOrder("order-1", "alice"), TestContext.Current.CancellationToken);
            return unitOfWork;
        }

        static string? State(FakeLogRecord record, string key) =>
            record.StructuredState!.Single(entry => entry.Key == key).Value;

        [Fact]
        public async Task GivenANameTheRegistryDoesNotKnow_WarnsOncePerType() {
            var logger = new FakeLogger<MsspCommitHandler>();
            var options = new MsspOptions { Serialize = _ => new MsspEventData("Wrong.Name@v1", new byte[] { 1 }) };
            var handler = new MsspCommitHandler(NewClient(), options, logger, EventTypeRegistry.FromTypes([typeof(OrderPlaced)]));

            await handler.CommitAsync(await ChangedUnitOfWork(), TestContext.Current.CancellationToken);
            await handler.CommitAsync(await ChangedUnitOfWork(), TestContext.Current.CancellationToken);

            var warning = logger.Collector.GetSnapshot().Single(record => record.Level == LogLevel.Warning && record.Message.Contains("names it"));
            State(warning, "Type").Should().Be(typeof(OrderPlaced).FullName);
            State(warning, "StoredName").Should().Be("Wrong.Name@v1");
            State(warning, "RegistryName").Should().Be("IntegrationTests.OrderPlaced@v1");
        }

        [Fact]
        public async Task GivenAnEventTypeOutsideTheRegistry_WarnsOnceThatItCanNotBeReadBack() {
            var logger = new FakeLogger<MsspCommitHandler>();
            var registry = EventTypeRegistry.FromTypes([typeof(Shipped)]);
            var options = new MsspOptions { Serialize = @event => new MsspEventData(registry.GetTypeName(@event.GetType()), new byte[] { 1 }) };
            var handler = new MsspCommitHandler(NewClient(), options, logger, registry);

            await handler.CommitAsync(await ChangedUnitOfWork(), TestContext.Current.CancellationToken);
            await handler.CommitAsync(await ChangedUnitOfWork(), TestContext.Current.CancellationToken);

            var warning = logger.Collector.GetSnapshot().Single(record => record.Level == LogLevel.Warning && record.Message.Contains("can't be read back"));
            State(warning, "Type").Should().Be(typeof(OrderPlaced).FullName);
            State(warning, "StoredName").Should().Be("IntegrationTests.OrderPlaced@v1");
        }

        [Fact]
        public async Task GivenAConcurrencyConflict_ReportsNoActualVersion() {
            var client = A.Fake<IMsspClient>();
            A.CallTo(() => client.AppendAsync(A<MsspStreamId>._, A<MsspStreamRevision>._, A<IEnumerable<MsspEventData>>._, A<CancellationToken>._))
                .Throws(new global::MSSP.OptimisticConcurrencyException("order-1", 0UL));
            var handler = new MsspCommitHandler(client, new MsspOptions {
                Serialize = @event => new MsspEventData("IntegrationTests.OrderPlaced@v1", new byte[] { 1 })
            }, new FakeLogger<MsspCommitHandler>());
            var unitOfWork = new UnitOfWork();
            var root = new EntityRoot<OrderState, IOrderEvent>(OrderState.Initial, new AggregateVersion(3));
            unitOfWork.Attach(new Aggregate("order-1", root));
            await root.AcceptAsync(new PlaceOrder("order-1", "alice"), TestContext.Current.CancellationToken);

            var act = () => handler.CommitAsync(unitOfWork, TestContext.Current.CancellationToken).AsTask();

            var failure = (await act.Should().ThrowAsync<ConcurrencyException>()).Which;
            failure.Identifier.Value.Should().Be("order-1");
            failure.ExpectedVersion.Should().Be(new AggregateVersion(3));
            failure.ActualVersion.Should().Be(AggregateVersion.None);
        }

        [Fact]
        public async Task GivenTheRegistrysName_DoesNotWarn() {
            var logger = new FakeLogger<MsspCommitHandler>();
            var registry = EventTypeRegistry.FromTypes([typeof(OrderPlaced)]);
            var options = new MsspOptions { Serialize = @event => new MsspEventData(registry.GetTypeName(@event.GetType()), new byte[] { 1 }) };
            var handler = new MsspCommitHandler(NewClient(), options, logger, registry);

            await handler.CommitAsync(await ChangedUnitOfWork(), TestContext.Current.CancellationToken);

            logger.Collector.GetSnapshot().Should().NotContain(record => record.Level == LogLevel.Warning);
        }

        [Fact]
        public async Task WithoutRegistry_DoesNotWarn() {
            var logger = new FakeLogger<MsspCommitHandler>();
            var options = new MsspOptions { Serialize = _ => new MsspEventData("Wrong.Name@v1", new byte[] { 1 }) };
            var handler = new MsspCommitHandler(NewClient(), options, logger);

            await handler.CommitAsync(await ChangedUnitOfWork(), TestContext.Current.CancellationToken);

            logger.Collector.GetSnapshot().Should().NotContain(record => record.Level == LogLevel.Warning);
        }

        [Fact]
        public async Task GivenASagaCommit_DoesNotCheck() {
            var logger = new FakeLogger<MsspCommitHandler>();
            var options = new MsspOptions { Serialize = _ => new MsspEventData("IntegrationTests.OrderPlaced@v1", new byte[] { 1 }) };
            var handler = new MsspCommitHandler(NewClient(), options, logger, EventTypeRegistry.FromTypes([typeof(OrderPlaced)]));

            await handler.CommitAsync(await ChangedUnitOfWork(), _ => new MsspEventData("Wrong.Name@v1", new byte[] { 1 }), TestContext.Current.CancellationToken);

            logger.Collector.GetSnapshot().Should().NotContain(record => record.Level == LogLevel.Warning);
        }

        [Fact]
        public async Task PassesTheCancellationTokenToTheAppend() {
            var client = NewClient();
            using var cts = new CancellationTokenSource();
            var logger = new FakeLogger<MsspCommitHandler>();
            var options = new MsspOptions { Serialize = _ => new MsspEventData("IntegrationTests.OrderPlaced@v1", new byte[] { 1 }) };
            var handler = new MsspCommitHandler(client, options, logger);

            await handler.CommitAsync(await ChangedUnitOfWork(), cts.Token);

            A.CallTo(() => client.AppendAsync(A<MsspStreamId>._, A<MsspStreamRevision>._, A<IEnumerable<MsspEventData>>._, cts.Token)).MustHaveHappenedOnceExactly();
        }

        [Fact]
        public async Task GivenASagaCommit_PassesTheCancellationTokenToTheAppend() {
            var client = NewClient();
            using var cts = new CancellationTokenSource();
            var logger = new FakeLogger<MsspCommitHandler>();
            var options = new MsspOptions { Serialize = _ => new MsspEventData("IntegrationTests.OrderPlaced@v1", new byte[] { 1 }) };
            var handler = new MsspCommitHandler(client, options, logger);

            await handler.CommitAsync(await ChangedUnitOfWork(), _ => new MsspEventData("$>", new byte[] { 1 }), cts.Token);

            A.CallTo(() => client.AppendAsync(A<MsspStreamId>._, A<MsspStreamRevision>._, A<IEnumerable<MsspEventData>>._, cts.Token)).MustHaveHappenedOnceExactly();
        }
    }

    public class GetEndPosition {
        // A default interface method, so it is only reachable through the interface.
        readonly ISubscriptionFactory _factory = new MsspSubscriptionFactory(A.Fake<IMsspClient>(), new MsspOptions());

        [Fact]
        public async Task ReturnsNull() {
            (await _factory.GetEndPositionAsync(TestContext.Current.CancellationToken)).Should().BeNull();
        }
    }
}
