using FluentAssertions;

namespace Aggregates.Projections;

public class ProjectionContractAttributeTests {

    public class ToString {
        [Fact]
        public void WithNameOnly_ReturnsNameAtVersion1() {
            var attr = new ProjectionContractAttribute("Orders");

            attr.ToString().Should().Be("Orders@v1");
        }

        [Fact]
        public void WithNameAndVersion_ReturnsNameAtVersion() {
            var attr = new ProjectionContractAttribute("Orders", version: 3);

            attr.ToString().Should().Be("Orders@v3");
        }

        [Fact]
        public void WithNamespace_PrefixesNamespace() {
            var attr = new ProjectionContractAttribute("Orders", @namespace: "Fulfillment");

            attr.ToString().Should().Be("Fulfillment.Orders@v1");
        }

        [Fact]
        public void WithNamespaceAndVersion_IncludesBoth() {
            var attr = new ProjectionContractAttribute("Orders", version: 2, @namespace: "Fulfillment");

            attr.ToString().Should().Be("Fulfillment.Orders@v2");
        }

        [Fact]
        public void WithEmptyNamespace_OmitsNamespacePrefix() {
            var attr = new ProjectionContractAttribute("Orders", @namespace: "");

            attr.ToString().Should().Be("Orders@v1");
        }
    }

    public class StartFromEnd {
        [Fact]
        public void DefaultsToFalse() {
            var attr = new ProjectionContractAttribute("Orders");

            attr.StartFromEnd.Should().BeFalse();
        }

        [Fact]
        public void WhenSetToTrue_ReturnsTrue() {
            var attr = new ProjectionContractAttribute("Orders", startFromEnd: true);

            attr.StartFromEnd.Should().BeTrue();
        }
    }
}
