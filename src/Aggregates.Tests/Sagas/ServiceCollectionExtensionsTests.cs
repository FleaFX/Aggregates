using Aggregates.Subscriptions;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace Aggregates.Sagas;

public class ServiceCollectionExtensionsTests {

    public class AddSagas {
        [Fact]
        public void ScanTypes_RegistersOnlyTheGivenSagas() {
            var services = new ServiceCollection();

            services.AddAggregates().AddSagas(o => o
                .ScanTypes(typeof(TestSaga))
                .WithResolver<TestEvent>(_ => [])
                .WithResolver<OtherTestEvent>(_ => []));

            services.Should().ContainSingle(d =>
                d.ServiceType == typeof(ISaga<TestSagaState, TestEvent>) && d.ImplementationType == typeof(TestSaga));
            services.Should().NotContain(d => d.ServiceType == typeof(ISaga<OtherTestSagaState, OtherTestEvent>));
            services.Should().ContainSingle(d => d.ServiceType == typeof(IHostedService));
        }

        [Fact]
        public void ScanTypes_SameTypeTwice_StartsOneSubscription() {
            var services = new ServiceCollection();

            services.AddAggregates().AddSagas(o => o
                .ScanTypes(typeof(TestSaga), typeof(TestSaga))
                .WithResolver<TestEvent>(_ => []));

            services.Should().ContainSingle(d => d.ServiceType == typeof(IHostedService));
        }

        [Fact]
        public void ScanTypes_IgnoresTypesThatAreNotSagas() {
            var services = new ServiceCollection();

            services.AddAggregates().AddSagas(o => o
                .ScanTypes(typeof(string))
                .WithResolver<TestEvent>(_ => []));

            services.Should().NotContain(d => d.ServiceType == typeof(IHostedService));
        }

        [Fact]
        public void ScanAssemblies_RegistersEverySagaInTheAssembly() {
            var services = new ServiceCollection();

            services.AddAggregates().AddSagas(o => o.ScanAssemblies(typeof(TestSaga).Assembly));

            services.Should().ContainSingle(d =>
                d.ServiceType == typeof(ISaga<TestSagaState, TestEvent>) && d.ImplementationType == typeof(TestSaga));
            services.Should().ContainSingle(d =>
                d.ServiceType == typeof(ISaga<OtherTestSagaState, OtherTestEvent>) && d.ImplementationType == typeof(OtherTestSaga));
        }

        [Fact]
        public void RegistersDefaultCheckpointOptionsAndSystemTimeProvider() {
            var services = new ServiceCollection();

            services.AddAggregates().AddSagas();

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

            services.AddAggregates().AddSagas();

            services.Should().ContainSingle(d => d.ServiceType == typeof(SubscriptionCheckpointOptions))
                .Which.ImplementationInstance.Should().BeSameAs(options);
        }

        [Fact]
        public void RegistersSubscriptionLoopWithDefaultResubscribeOptions() {
            var services = new ServiceCollection();

            services.AddAggregates().AddSagas();

            services.Should().ContainSingle(d => d.ServiceType == typeof(SubscriptionLoop))
                .Which.Lifetime.Should().Be(ServiceLifetime.Singleton);
            using var provider = services.BuildServiceProvider();
            provider.GetRequiredService<SubscriptionResubscribeOptions>().MaxDelay.Should().Be(TimeSpan.FromSeconds(30));
        }
    }
}
