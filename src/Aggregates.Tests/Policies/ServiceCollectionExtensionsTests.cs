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
    }
}
