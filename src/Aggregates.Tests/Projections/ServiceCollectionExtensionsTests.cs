using Aggregates.Subscriptions;
using Aggregates.Testing;
using FakeItEasy;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace Aggregates.Projections;

public class ServiceCollectionExtensionsTests {

    public class AddProjections {
        static IEnumerable<SubscriptionRegistration> Registrations(IServiceCollection services) =>
            services.Select(d => d.ImplementationInstance).OfType<SubscriptionRegistration>();

        [Fact]
        public void ScanTypes_RegistersOnlyTheGivenProjections() {
            var services = new ServiceCollection();

            services.AddProjections(o => o.ScanTypes(typeof(TestProjection)));

            services.Should().ContainSingle(d => d.ServiceType == typeof(TestProjection));
            services.Should().NotContain(d => d.ServiceType == typeof(OtherTestProjection));
            services.Should().ContainSingle(d => d.ServiceType == typeof(IHostedService));
        }

        [Fact]
        public void RegistersTheProjectionAsItself_NotAsItsInterface() {
            var services = new ServiceCollection();

            services.AddProjections(o => o.ScanTypes(typeof(TestProjection)));

            services.Should().NotContain(d => d.ServiceType == typeof(IProjection<ProjectionTestEvent>));
        }

        [Fact]
        public void ScanTypes_SameTypeTwice_StartsOneSubscription() {
            var services = new ServiceCollection();

            services.AddProjections(o => o.ScanTypes(typeof(TestProjection), typeof(TestProjection)));

            services.Should().ContainSingle(d => d.ServiceType == typeof(IHostedService));
        }

        [Fact]
        public void CalledTwiceWithTheSameType_StartsOneSubscription() {
            var services = new ServiceCollection();

            services.AddProjections(o => o.ScanTypes(typeof(TestProjection)));
            services.AddProjections(o => o.ScanTypes(typeof(TestProjection)));

            services.Should().ContainSingle(d => d.ServiceType == typeof(IHostedService));
            Registrations(services).Should().ContainSingle();
        }

        [Fact]
        public void ScanTypes_IgnoresTypesThatAreNotProjections() {
            var services = new ServiceCollection();

            services.AddProjections(o => o.ScanTypes(typeof(string)));

            services.Should().NotContain(d => d.ServiceType == typeof(IHostedService));
        }

        // Registering the whole test assembly would fail on DoubleProjection, so this checks what gets scanned.
        [Fact]
        public void ScanAssemblies_InspectsEveryTypeInTheAssembly() {
            var options = new ProjectionsOptions().ScanAssemblies(typeof(TestProjection).Assembly);

            options.Types.Should().Contain([typeof(TestProjection), typeof(OtherTestProjection)]);
        }

        [Theory]
        [InlineData(typeof(PlainContractProjection), "Orders@v1")]
        [InlineData(typeof(FullContractProjection), "Fulfillment.Orders@v2")]
        [InlineData(typeof(TestProjection), "Aggregates.Projections.TestProjection")]
        [InlineData(typeof(NestedProjection), "Aggregates.Projections.ServiceCollectionExtensionsTests+AddProjections+NestedProjection")]
        public void SubscriptionId_IsTheContract_OrTheFullTypeName(Type projectionType, string expectedId) {
            var services = new ServiceCollection();

            services.AddProjections(o => o.ScanTypes(projectionType));

            Registrations(services).Should().ContainSingle().Which.SubscriptionId.Should().Be(expectedId);
        }

        [Fact]
        public void Registration_HasTheProjectionEventTypeAndStartFromEnd() {
            var services = new ServiceCollection();

            services.AddProjections(o => o.ScanTypes(typeof(FullContractProjection)));

            Registrations(services).Should().Equal(
                new SubscriptionRegistration("Fulfillment.Orders@v2", typeof(FullContractProjection), typeof(ProjectionTestEvent), StartFromEnd: true));
        }

        [Fact]
        public void GivenAnObjectProjection_RegistersObjectAsEventType() {
            var services = new ServiceCollection();

            services.AddProjections(o => o.ScanTypes(typeof(CatchAllProjection)));

            Registrations(services).Should().ContainSingle().Which.EventType.Should().Be(typeof(object));
        }

        [Fact]
        public void GivenAClassWithTwoProjectionInterfaces_Throws() {
            var add = () => new ServiceCollection().AddProjections(o => o.ScanTypes(typeof(DoubleProjection)));

            add.Should().Throw<InvalidOperationException>()
                .WithMessage($"*{typeof(DoubleProjection).FullName}*IProjection<TEvent> once*");
        }

        [Fact]
        public void GivenTwoClassesWithTheSameContract_Throws() {
            var add = () => new ServiceCollection().AddProjections(o => o.ScanTypes(typeof(PlainContractProjection), typeof(SameContractProjection)));

            add.Should().Throw<InvalidOperationException>().WithMessage("*'Orders@v1'*");
        }

        [Fact]
        public async Task GivenTwoProjectionsOnTheSameEvent_EachSubscriptionRunsItsOwnProjectionOnce() {
            var token = TestContext.Current.CancellationToken;
            var factory = new FakeSubscriptionFactory(FakeSubscriptionFactory.Messages(1, _ => new ProjectionTestEvent(1)));
            var services = new ServiceCollection()
                .AddLogging()
                .AddSingleton<HandlerProbe>()
                .AddSingleton<ISubscriptionFactory>(factory)
                .AddSingleton(A.Fake<ICheckpointStore>());
            services.AddProjections(o => o.ScanTypes(typeof(PlainContractProjection), typeof(FullContractProjection)));
            await using var provider = services.BuildServiceProvider();
            var probe = provider.GetRequiredService<HandlerProbe>();
            var hostedServices = provider.GetServices<IHostedService>().ToArray();

            foreach (var hostedService in hostedServices)
                await hostedService.StartAsync(token);
            await Eventually.UntilAsync(() => probe.Count<PlainContractProjection>() >= 1 && probe.Count<FullContractProjection>() >= 1,
                "both projections handle the event");
            foreach (var hostedService in hostedServices)
                await hostedService.StopAsync(token);

            hostedServices.Should().HaveCount(2);
            probe.Count<PlainContractProjection>().Should().Be(1);
            probe.Count<FullContractProjection>().Should().Be(1);
        }

        [Fact]
        public void RegistersDefaultCheckpointOptionsAndSystemTimeProvider() {
            var services = new ServiceCollection();

            services.AddProjections();

            using var provider = services.BuildServiceProvider();
            var options = provider.GetRequiredService<SubscriptionCheckpointOptions>();
            options.MaxBatchSize.Should().Be(100);
            options.MaxInterval.Should().Be(TimeSpan.FromSeconds(5));
            provider.GetRequiredService<TimeProvider>().Should().BeSameAs(TimeProvider.System);
        }

        [Fact]
        public void KeepsCheckpointOptionsRegisteredBefore() {
            var services = new ServiceCollection();
            var options = new SubscriptionCheckpointOptions { MaxBatchSize = 10 };
            services.AddSingleton(options);

            services.AddProjections();

            services.Should().ContainSingle(d => d.ServiceType == typeof(SubscriptionCheckpointOptions))
                .Which.ImplementationInstance.Should().BeSameAs(options);
        }

        [Fact]
        public void RegistersSubscriptionLoopWithDefaultResubscribeOptions() {
            var services = new ServiceCollection();

            services.AddProjections();

            services.Should().ContainSingle(d => d.ServiceType == typeof(SubscriptionLoop))
                .Which.Lifetime.Should().Be(ServiceLifetime.Singleton);
            using var provider = services.BuildServiceProvider();
            provider.GetRequiredService<SubscriptionResubscribeOptions>().MaxDelay.Should().Be(TimeSpan.FromSeconds(30));
        }

        abstract class ProbeProjection<TEvent>(HandlerProbe probe) : IProjection<TEvent> {
            public ValueTask<ICommit> ProjectAsync(TEvent @event, EventMetadata metadata, CancellationToken cancellationToken = default) {
                probe.Record(this, @event!, metadata);
                return ValueTask.FromResult(Commit.Create());
            }
        }

        [ProjectionContract("Orders")]
        sealed class PlainContractProjection(HandlerProbe probe) : ProbeProjection<ProjectionTestEvent>(probe);

        [ProjectionContract("Orders")]
        sealed class SameContractProjection(HandlerProbe probe) : ProbeProjection<ProjectionTestEvent>(probe);

        [ProjectionContract("Orders", version: 2, @namespace: "Fulfillment", startFromEnd: true)]
        sealed class FullContractProjection(HandlerProbe probe) : ProbeProjection<ProjectionTestEvent>(probe);

        sealed class NestedProjection(HandlerProbe probe) : ProbeProjection<ProjectionTestEvent>(probe);

        [ProjectionContract("CatchAll")]
        sealed class CatchAllProjection(HandlerProbe probe) : ProbeProjection<object>(probe);

        [ProjectionContract("Double")]
        sealed class DoubleProjection(HandlerProbe probe) : ProbeProjection<ProjectionTestEvent>(probe), IProjection<OtherProjectionTestEvent> {
            public ValueTask<ICommit> ProjectAsync(OtherProjectionTestEvent @event, EventMetadata metadata, CancellationToken cancellationToken = default) =>
                ValueTask.FromResult(Commit.Create());
        }
    }
}
