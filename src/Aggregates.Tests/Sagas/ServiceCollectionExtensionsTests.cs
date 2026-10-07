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
    }
}
