using Aggregates.Subscriptions;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace Aggregates.Projections;

public class ServiceCollectionExtensionsTests {

    public class AddProjections {
        [Fact]
        public void ScanTypes_RegistersOnlyTheGivenProjections() {
            var services = new ServiceCollection();

            services.AddProjections(o => o.ScanTypes(typeof(TestProjection)));

            services.Should().ContainSingle(d =>
                d.ServiceType == typeof(IProjection<ProjectionTestEvent>) && d.ImplementationType == typeof(TestProjection));
            services.Should().NotContain(d => d.ServiceType == typeof(IProjection<OtherProjectionTestEvent>));
            services.Should().ContainSingle(d => d.ServiceType == typeof(IHostedService));
        }

        [Fact]
        public void ScanTypes_SameTypeTwice_StartsOneSubscription() {
            var services = new ServiceCollection();

            services.AddProjections(o => o.ScanTypes(typeof(TestProjection), typeof(TestProjection)));

            services.Should().ContainSingle(d => d.ServiceType == typeof(IHostedService));
        }

        [Fact]
        public void ScanTypes_IgnoresTypesThatAreNotProjections() {
            var services = new ServiceCollection();

            services.AddProjections(o => o.ScanTypes(typeof(string)));

            services.Should().NotContain(d => d.ServiceType == typeof(IHostedService));
        }

        [Fact]
        public void ScanAssemblies_RegistersEveryProjectionInTheAssembly() {
            var services = new ServiceCollection();

            services.AddProjections(o => o.ScanAssemblies(typeof(TestProjection).Assembly));

            services.Should().ContainSingle(d =>
                d.ServiceType == typeof(IProjection<ProjectionTestEvent>) && d.ImplementationType == typeof(TestProjection));
            services.Should().ContainSingle(d =>
                d.ServiceType == typeof(IProjection<OtherProjectionTestEvent>) && d.ImplementationType == typeof(OtherTestProjection));
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
    }
}
