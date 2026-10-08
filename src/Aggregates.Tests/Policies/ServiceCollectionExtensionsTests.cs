using System.Runtime.CompilerServices;
using Aggregates.Projections;
using Aggregates.Subscriptions;
using Aggregates.Testing;
using FakeItEasy;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace Aggregates.Policies;

public class ServiceCollectionExtensionsTests {

    public class AddPolicies {
        static IEnumerable<SubscriptionRegistration> Registrations(IServiceCollection services) =>
            services.Select(d => d.ImplementationInstance).OfType<SubscriptionRegistration>();

        [Fact]
        public void ScanTypes_RegistersOnlyTheGivenPolicies() {
            var services = new ServiceCollection();

            services.AddAggregates().AddPolicies(o => o.ScanTypes(typeof(TestPolicy)));

            services.Should().ContainSingle(d => d.ServiceType == typeof(TestPolicy));
            services.Should().NotContain(d => d.ServiceType == typeof(OtherTestPolicy));
            services.Should().ContainSingle(d => d.ServiceType == typeof(IHostedService));
        }

        [Fact]
        public void RegistersThePolicyAsItself_NotAsItsInterface() {
            var services = new ServiceCollection();

            services.AddAggregates().AddPolicies(o => o.ScanTypes(typeof(TestPolicy)));

            services.Should().NotContain(d => d.ServiceType == typeof(IPolicy<PolicyTestEvent>));
        }

        [Fact]
        public void ScanTypes_SameTypeTwice_StartsOneSubscription() {
            var services = new ServiceCollection();

            services.AddAggregates().AddPolicies(o => o.ScanTypes(typeof(TestPolicy), typeof(TestPolicy)));

            services.Should().ContainSingle(d => d.ServiceType == typeof(IHostedService));
        }

        [Fact]
        public void CalledTwiceWithTheSameType_StartsOneSubscription() {
            var services = new ServiceCollection();
            var builder = services.AddAggregates();

            builder.AddPolicies(o => o.ScanTypes(typeof(TestPolicy)));
            builder.AddPolicies(o => o.ScanTypes(typeof(TestPolicy)));

            services.Should().ContainSingle(d => d.ServiceType == typeof(IHostedService));
            Registrations(services).Should().ContainSingle();
        }

        [Fact]
        public void ScanTypes_IgnoresTypesThatAreNotPolicies() {
            var services = new ServiceCollection();

            services.AddAggregates().AddPolicies(o => o.ScanTypes(typeof(string)));

            services.Should().NotContain(d => d.ServiceType == typeof(IHostedService));
        }

        // Registering the whole test assembly would fail on DoublePolicy, so this checks what gets scanned.
        [Fact]
        public void ScanAssemblies_InspectsEveryTypeInTheAssembly() {
            var options = new PoliciesOptions().ScanAssemblies(typeof(TestPolicy).Assembly);

            options.Types.Should().Contain([typeof(TestPolicy), typeof(OtherTestPolicy)]);
        }

        [Theory]
        [InlineData(typeof(PlainContractPolicy), "Shipping@v1")]
        [InlineData(typeof(FullContractPolicy), "Fulfillment.Shipping@v2")]
        [InlineData(typeof(TestPolicy), "Aggregates.Policies.TestPolicy")]
        public void SubscriptionId_IsTheContract_OrTheFullTypeName(Type policyType, string expectedId) {
            var services = new ServiceCollection();

            services.AddAggregates().AddPolicies(o => o.ScanTypes(policyType));

            Registrations(services).Should().ContainSingle().Which.SubscriptionId.Should().Be(expectedId);
        }

        [Fact]
        public void Registration_HasThePolicyEventTypeAndStartFromEnd() {
            var services = new ServiceCollection();

            services.AddAggregates().AddPolicies(o => o.ScanTypes(typeof(FullContractPolicy)));

            Registrations(services).Should().Equal(
                new SubscriptionRegistration("Fulfillment.Shipping@v2", typeof(FullContractPolicy), typeof(PolicyTestEvent), StartFromEnd: true));
        }

        [Fact]
        public void GivenAClassWithTwoPolicyInterfaces_Throws() {
            var add = () => new ServiceCollection().AddAggregates().AddPolicies(o => o.ScanTypes(typeof(DoublePolicy)));

            add.Should().Throw<InvalidOperationException>()
                .WithMessage($"*{typeof(DoublePolicy).FullName}*IPolicy<TEvent> once*");
        }

        [Fact]
        public void GivenTwoClassesWithTheSameContract_Throws() {
            var add = () => new ServiceCollection().AddAggregates()
                .AddPolicies(o => o.ScanTypes(typeof(PlainContractPolicy), typeof(SameContractPolicy)));

            add.Should().Throw<InvalidOperationException>().WithMessage("*'Shipping@v1'*");
        }

        [Fact]
        public void GivenAProjectionWithTheSameContract_Throws() {
            var services = new ServiceCollection();
            services.AddProjections(o => o.ScanTypes(typeof(ShippingProjection)));

            var add = () => services.AddAggregates().AddPolicies(o => o.ScanTypes(typeof(PlainContractPolicy)));

            add.Should().Throw<InvalidOperationException>()
                .WithMessage($"*'Shipping@v1'*{typeof(ShippingProjection).FullName}*{typeof(PlainContractPolicy).FullName}*");
        }

        [Fact]
        public async Task GivenTwoPoliciesOnTheSameEvent_EachSubscriptionRunsItsOwnPolicyOnce() {
            var token = TestContext.Current.CancellationToken;
            var factory = new FakeSubscriptionFactory(FakeSubscriptionFactory.Messages(1, _ => new PolicyTestEvent(1)));
            var services = new ServiceCollection()
                .AddLogging()
                .AddSingleton<HandlerProbe>()
                .AddSingleton<ISubscriptionFactory>(factory)
                .AddSingleton(A.Fake<ICheckpointStore>());
            services.AddAggregates().AddPolicies(o => o.ScanTypes(typeof(PlainContractPolicy), typeof(FullContractPolicy)));
            await using var provider = services.BuildServiceProvider();
            var probe = provider.GetRequiredService<HandlerProbe>();
            var hostedServices = provider.GetServices<IHostedService>().ToArray();

            foreach (var hostedService in hostedServices)
                await hostedService.StartAsync(token);
            await Eventually.UntilAsync(() => probe.Count<PlainContractPolicy>() >= 1 && probe.Count<FullContractPolicy>() >= 1,
                "both policies handle the event");
            foreach (var hostedService in hostedServices)
                await hostedService.StopAsync(token);

            hostedServices.Should().HaveCount(2);
            probe.Count<PlainContractPolicy>().Should().Be(1);
            probe.Count<FullContractPolicy>().Should().Be(1);
        }

        [Fact]
        public void RegistersDefaultCheckpointOptionsAndSystemTimeProvider() {
            var services = new ServiceCollection();

            services.AddAggregates().AddPolicies();

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

            services.AddAggregates().AddPolicies();

            services.Should().ContainSingle(d => d.ServiceType == typeof(SubscriptionCheckpointOptions))
                .Which.ImplementationInstance.Should().BeSameAs(options);
        }

        [Fact]
        public void RegistersSubscriptionLoopWithDefaultResubscribeOptions() {
            var services = new ServiceCollection();

            services.AddAggregates().AddPolicies();

            services.Should().ContainSingle(d => d.ServiceType == typeof(SubscriptionLoop))
                .Which.Lifetime.Should().Be(ServiceLifetime.Singleton);
            using var provider = services.BuildServiceProvider();
            provider.GetRequiredService<SubscriptionResubscribeOptions>().MaxDelay.Should().Be(TimeSpan.FromSeconds(30));
        }

        abstract class ProbePolicy<TEvent>(HandlerProbe probe) : IPolicy<TEvent> {
            public async IAsyncEnumerable<ICommand> ReactAsync(TEvent @event, [EnumeratorCancellation] CancellationToken cancellationToken = default) {
                probe.Record(this, @event!);
                yield break;
            }
        }

        [PolicyContract("Shipping")]
        sealed class PlainContractPolicy(HandlerProbe probe) : ProbePolicy<PolicyTestEvent>(probe);

        [PolicyContract("Shipping")]
        sealed class SameContractPolicy(HandlerProbe probe) : ProbePolicy<PolicyTestEvent>(probe);

        [PolicyContract("Shipping", version: 2, @namespace: "Fulfillment", startFromEnd: true)]
        sealed class FullContractPolicy(HandlerProbe probe) : ProbePolicy<PolicyTestEvent>(probe);

        [PolicyContract("Double")]
        sealed class DoublePolicy(HandlerProbe probe) : ProbePolicy<PolicyTestEvent>(probe), IPolicy<OtherPolicyTestEvent> {
            public IAsyncEnumerable<ICommand> ReactAsync(OtherPolicyTestEvent @event, CancellationToken cancellationToken = default) =>
                AsyncEnumerable.Empty<ICommand>();
        }

        [ProjectionContract("Shipping")]
        sealed class ShippingProjection : IProjection<PolicyTestEvent> {
            public ValueTask<ICommit> ProjectAsync(PolicyTestEvent @event, EventMetadata metadata, CancellationToken cancellationToken = default) =>
                ValueTask.FromResult(Commit.Create());
        }
    }
}
