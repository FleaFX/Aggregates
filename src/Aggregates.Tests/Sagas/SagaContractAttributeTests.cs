using FluentAssertions;

namespace Aggregates.Sagas;

public class SagaContractAttributeTests {

    public class ToString {
        [Fact]
        public void WithNameOnly_ReturnsNameAtVersion1() {
            var attr = new SagaContractAttribute("OrderFulfillment");

            attr.ToString().Should().Be("OrderFulfillment@v1");
        }

        [Fact]
        public void WithNameAndVersion_ReturnsNameAtVersion() {
            var attr = new SagaContractAttribute("OrderFulfillment", version: 3);

            attr.ToString().Should().Be("OrderFulfillment@v3");
        }

        [Fact]
        public void WithNamespace_PrefixesNamespace() {
            var attr = new SagaContractAttribute("OrderFulfillment", @namespace: "Orders");

            attr.ToString().Should().Be("Orders.OrderFulfillment@v1");
        }

        [Fact]
        public void WithNamespaceAndVersion_IncludesBoth() {
            var attr = new SagaContractAttribute("OrderFulfillment", version: 2, @namespace: "Orders");

            attr.ToString().Should().Be("Orders.OrderFulfillment@v2");
        }

        [Fact]
        public void WithEmptyNamespace_OmitsNamespacePrefix() {
            var attr = new SagaContractAttribute("OrderFulfillment", @namespace: "");

            attr.ToString().Should().Be("OrderFulfillment@v1");
        }
    }

    public class ContinueFrom {
        [Fact]
        public void WhenVersion1_ReturnsNull() {
            var attr = new SagaContractAttribute("OrderFulfillment", version: 1);

            attr.ContinueFrom.Should().BeNull();
        }

        [Fact]
        public void WhenVersion2_DefaultsToPreviousVersion() {
            var attr = new SagaContractAttribute("OrderFulfillment", version: 2);

            attr.ContinueFrom.Should().Be("OrderFulfillment@v1");
        }

        [Fact]
        public void WhenVersion2WithNamespace_IncludesNamespaceInDefault() {
            var attr = new SagaContractAttribute("OrderFulfillment", version: 2, @namespace: "Orders");

            attr.ContinueFrom.Should().Be("Orders.OrderFulfillment@v1");
        }

        [Fact]
        public void WhenExplicitlySet_ReturnsExplicitValue() {
            var attr = new SagaContractAttribute("OrderFulfillment", version: 2, continueFrom: "Legacy.OrderFulfillment@v1");

            attr.ContinueFrom.Should().Be("Legacy.OrderFulfillment@v1");
        }
    }

    public class StartFromEnd {
        [Fact]
        public void DefaultsToFalse() {
            var attr = new SagaContractAttribute("OrderFulfillment");

            attr.StartFromEnd.Should().BeFalse();
        }

        [Fact]
        public void WhenSetToTrue_ReturnsTrue() {
            var attr = new SagaContractAttribute("OrderFulfillment", startFromEnd: true);

            attr.StartFromEnd.Should().BeTrue();
        }
    }
}
