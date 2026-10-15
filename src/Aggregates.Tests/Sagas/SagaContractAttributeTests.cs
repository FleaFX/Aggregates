using FluentAssertions;

namespace Aggregates.Sagas;

public class SagaContractAttributeTests {

    public new class ToString {
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

        [Fact]
        public void WhenEmpty_ReturnsNull() {
            var attr = new SagaContractAttribute("OrderFulfillment", version: 3, continueFrom: "");

            attr.ContinueFrom.Should().BeNull();
        }

        [Fact]
        public void WhenNone_ReturnsNull() {
            var attr = new SagaContractAttribute("OrderFulfillment", version: 3,
                continueFrom: Aggregates.Subscriptions.ContinueFrom.None);

            attr.ContinueFrom.Should().BeNull();
        }
    }

    public class Predecessors {
        [Fact]
        public void WhenVersion1_IsEmpty() {
            var attr = new SagaContractAttribute("OrderFulfillment", version: 1);

            attr.Predecessors.Should().BeEmpty();
        }

        [Fact]
        public void WhenVersion2_ContainsPreviousVersion() {
            var attr = new SagaContractAttribute("OrderFulfillment", version: 2);

            attr.Predecessors.Should().Equal("OrderFulfillment@v1");
        }

        [Fact]
        public void WhenVersion3_ContainsEveryPreviousVersionNearestFirst() {
            var attr = new SagaContractAttribute("OrderFulfillment", version: 3);

            attr.Predecessors.Should().Equal("OrderFulfillment@v2", "OrderFulfillment@v1");
        }

        [Fact]
        public void WhenVersion3WithNamespace_IncludesNamespaceInEveryId() {
            var attr = new SagaContractAttribute("OrderFulfillment", version: 3, @namespace: "Orders");

            attr.Predecessors.Should().Equal("Orders.OrderFulfillment@v2", "Orders.OrderFulfillment@v1");
        }

        [Fact]
        public void WhenExplicitlySet_ContainsOnlyTheExplicitId() {
            var attr = new SagaContractAttribute("OrderFulfillment", version: 3, continueFrom: "Legacy.OrderFulfillment@v5");

            attr.Predecessors.Should().Equal("Legacy.OrderFulfillment@v5");
        }

        [Fact]
        public void WhenEmpty_IsEmpty() {
            var attr = new SagaContractAttribute("OrderFulfillment", version: 3, continueFrom: "");

            attr.Predecessors.Should().BeEmpty();
        }

        [Fact]
        public void WhenNone_IsEmpty() {
            var attr = new SagaContractAttribute("OrderFulfillment", version: 3,
                continueFrom: Aggregates.Subscriptions.ContinueFrom.None);

            attr.Predecessors.Should().BeEmpty();
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

        [Fact]
        public void WhenSetAlongsideContinueFrom_ReturnsTrue() {
            var attr = new SagaContractAttribute("OrderFulfillment", version: 2,
                continueFrom: "Legacy.OrderFulfillment@v5", startFromEnd: true);

            attr.StartFromEnd.Should().BeTrue();
        }
    }

    public class OnError {
        [Fact]
        public void DefaultsToPark() {
            var attr = new SagaContractAttribute("OrderFulfillment");

            attr.OnError.Should().Be(Aggregates.Subscriptions.SubscriptionErrorMode.Park);
        }

        [Fact]
        public void WhenSetToBlock_ReturnsBlock() {
            var attr = new SagaContractAttribute("OrderFulfillment", onError: Aggregates.Subscriptions.SubscriptionErrorMode.Block);

            attr.OnError.Should().Be(Aggregates.Subscriptions.SubscriptionErrorMode.Block);
        }
    }
}
