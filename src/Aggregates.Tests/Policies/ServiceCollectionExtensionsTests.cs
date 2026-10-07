using Aggregates.Subscriptions;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace Aggregates.Policies;

public class ServiceCollectionExtensionsTests {

    public class AddPolicies {
        [Fact]
        public void ScanTypes_RegistersOnlyTheGivenPolicies() {
            var services = new ServiceCollection();

            services.AddAggregates().AddPolicies(o => o.ScanTypes(typeof(TestPolicy)));

            services.Should().ContainSingle(d =>
                d.ServiceType == typeof(IPolicy<PolicyTestEvent>) && d.ImplementationType == typeof(TestPolicy));
            services.Should().NotContain(d => d.ServiceType == typeof(IPolicy<OtherPolicyTestEvent>));
            services.Should().ContainSingle(d => d.ServiceType == typeof(IHostedService));
        }

        [Fact]
        public void ScanTypes_SameTypeTwice_StartsOneSubscription() {
            var services = new ServiceCollection();

            services.AddAggregates().AddPolicies(o => o.ScanTypes(typeof(TestPolicy), typeof(TestPolicy)));

            services.Should().ContainSingle(d => d.ServiceType == typeof(IHostedService));
        }

        [Fact]
        public void ScanTypes_IgnoresTypesThatAreNotPolicies() {
            var services = new ServiceCollection();

            services.AddAggregates().AddPolicies(o => o.ScanTypes(typeof(string)));

            services.Should().NotContain(d => d.ServiceType == typeof(IHostedService));
        }

        [Fact]
        public void ScanAssemblies_RegistersEveryPolicyInTheAssembly() {
            var services = new ServiceCollection();

            services.AddAggregates().AddPolicies(o => o.ScanAssemblies(typeof(TestPolicy).Assembly));

            services.Should().ContainSingle(d =>
                d.ServiceType == typeof(IPolicy<PolicyTestEvent>) && d.ImplementationType == typeof(TestPolicy));
            services.Should().ContainSingle(d =>
                d.ServiceType == typeof(IPolicy<OtherPolicyTestEvent>) && d.ImplementationType == typeof(OtherTestPolicy));
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
    }
}
