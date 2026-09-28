using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;

namespace Aggregates;

public class ServiceCollectionExtensionsTests {

    public class AddAggregates {
        [Fact]
        public void ScanTypes_RegistersOnlyTheGivenCommands() {
            var services = new ServiceCollection();

            services.AddAggregates(o => o.ScanTypes(typeof(ScanTestCommand)));

            services.Should().ContainSingle(d => d.ServiceType == typeof(CommandHandler<ScanTestCommand>));
            services.Should().NotContain(d => d.ServiceType == typeof(CommandHandler<OtherScanTestCommand>));
        }

        [Fact]
        public void ScanTypes_IgnoresTypesThatAreNotCommands() {
            var services = new ServiceCollection();
            var baseline = new ServiceCollection();

            services.AddAggregates(o => o.ScanTypes(typeof(string)));
            baseline.AddAggregates();

            services.Should().HaveSameCount(baseline);
        }

        [Fact]
        public void ScanAssemblies_RegistersEveryCommandInTheAssembly() {
            var services = new ServiceCollection();

            services.AddAggregates(o => o.ScanAssemblies(typeof(ScanTestCommand).Assembly));

            services.Should().ContainSingle(d => d.ServiceType == typeof(CommandHandler<ScanTestCommand>));
            services.Should().ContainSingle(d => d.ServiceType == typeof(CommandHandler<OtherScanTestCommand>));
        }

        [Fact]
        public void ScanAssembliesAndScanTypes_WithOverlap_RegistersEachCommandOnce() {
            var services = new ServiceCollection();

            services.AddAggregates(o => o
                .ScanAssemblies(typeof(ScanTestCommand).Assembly)
                .ScanTypes(typeof(ScanTestCommand)));

            services.Should().ContainSingle(d => d.ServiceType == typeof(CommandHandler<ScanTestCommand>));
        }
    }
}
