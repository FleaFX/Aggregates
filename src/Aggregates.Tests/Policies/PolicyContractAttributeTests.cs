using FluentAssertions;

namespace Aggregates.Policies;

public class PolicyContractAttributeTests {

    public new class ToString {
        [Fact]
        public void WithNameOnly_ReturnsNameAtVersion1() {
            var attr = new PolicyContractAttribute("SendWelcomeEmail");

            attr.ToString().Should().Be("SendWelcomeEmail@v1");
        }

        [Fact]
        public void WithNameAndVersion_ReturnsNameAtVersion() {
            var attr = new PolicyContractAttribute("SendWelcomeEmail", version: 3);

            attr.ToString().Should().Be("SendWelcomeEmail@v3");
        }

        [Fact]
        public void WithNamespace_PrefixesNamespace() {
            var attr = new PolicyContractAttribute("SendWelcomeEmail", @namespace: "Notifications");

            attr.ToString().Should().Be("Notifications.SendWelcomeEmail@v1");
        }

        [Fact]
        public void WithNamespaceAndVersion_IncludesBoth() {
            var attr = new PolicyContractAttribute("SendWelcomeEmail", version: 2, @namespace: "Notifications");

            attr.ToString().Should().Be("Notifications.SendWelcomeEmail@v2");
        }

        [Fact]
        public void WithEmptyNamespace_OmitsNamespacePrefix() {
            var attr = new PolicyContractAttribute("SendWelcomeEmail", @namespace: "");

            attr.ToString().Should().Be("SendWelcomeEmail@v1");
        }
    }

    public class ContinueFrom {
        [Fact]
        public void WhenVersion1_ReturnsNull() {
            var attr = new PolicyContractAttribute("SendWelcomeEmail", version: 1);

            attr.ContinueFrom.Should().BeNull();
        }

        [Fact]
        public void WhenVersion2_DefaultsToPreviousVersion() {
            var attr = new PolicyContractAttribute("SendWelcomeEmail", version: 2);

            attr.ContinueFrom.Should().Be("SendWelcomeEmail@v1");
        }

        [Fact]
        public void WhenVersion2WithNamespace_IncludesNamespaceInDefault() {
            var attr = new PolicyContractAttribute("SendWelcomeEmail", version: 2, @namespace: "Notifications");

            attr.ContinueFrom.Should().Be("Notifications.SendWelcomeEmail@v1");
        }

        [Fact]
        public void WhenExplicitlySet_ReturnsExplicitValue() {
            var attr = new PolicyContractAttribute("SendWelcomeEmail", version: 3, continueFrom: "Legacy.SendWelcomeEmail@v5");

            attr.ContinueFrom.Should().Be("Legacy.SendWelcomeEmail@v5");
        }

        [Fact]
        public void WhenEmpty_ReturnsNull() {
            var attr = new PolicyContractAttribute("SendWelcomeEmail", version: 3, continueFrom: "");

            attr.ContinueFrom.Should().BeNull();
        }

        [Fact]
        public void WhenNone_ReturnsNull() {
            var attr = new PolicyContractAttribute("SendWelcomeEmail", version: 3,
                continueFrom: Aggregates.Subscriptions.ContinueFrom.None);

            attr.ContinueFrom.Should().BeNull();
        }
    }

    public class Predecessors {
        [Fact]
        public void WhenVersion1_IsEmpty() {
            var attr = new PolicyContractAttribute("SendWelcomeEmail", version: 1);

            attr.Predecessors.Should().BeEmpty();
        }

        [Fact]
        public void WhenVersion2_ContainsPreviousVersion() {
            var attr = new PolicyContractAttribute("SendWelcomeEmail", version: 2);

            attr.Predecessors.Should().Equal("SendWelcomeEmail@v1");
        }

        [Fact]
        public void WhenVersion3_ContainsEveryPreviousVersionNearestFirst() {
            var attr = new PolicyContractAttribute("SendWelcomeEmail", version: 3);

            attr.Predecessors.Should().Equal("SendWelcomeEmail@v2", "SendWelcomeEmail@v1");
        }

        [Fact]
        public void WhenVersion3WithNamespace_IncludesNamespaceInEveryId() {
            var attr = new PolicyContractAttribute("SendWelcomeEmail", version: 3, @namespace: "Notifications");

            attr.Predecessors.Should().Equal("Notifications.SendWelcomeEmail@v2", "Notifications.SendWelcomeEmail@v1");
        }

        [Fact]
        public void WhenExplicitlySet_ContainsOnlyTheExplicitId() {
            var attr = new PolicyContractAttribute("SendWelcomeEmail", version: 3, continueFrom: "Legacy.SendWelcomeEmail@v5");

            attr.Predecessors.Should().Equal("Legacy.SendWelcomeEmail@v5");
        }

        [Fact]
        public void WhenEmpty_IsEmpty() {
            var attr = new PolicyContractAttribute("SendWelcomeEmail", version: 3, continueFrom: "");

            attr.Predecessors.Should().BeEmpty();
        }

        [Fact]
        public void WhenNone_IsEmpty() {
            var attr = new PolicyContractAttribute("SendWelcomeEmail", version: 3,
                continueFrom: Aggregates.Subscriptions.ContinueFrom.None);

            attr.Predecessors.Should().BeEmpty();
        }
    }

    public class StartFromEnd {
        [Fact]
        public void DefaultsToFalse() {
            var attr = new PolicyContractAttribute("SendWelcomeEmail");

            attr.StartFromEnd.Should().BeFalse();
        }

        [Fact]
        public void WhenSetToTrue_ReturnsTrue() {
            var attr = new PolicyContractAttribute("SendWelcomeEmail", startFromEnd: true);

            attr.StartFromEnd.Should().BeTrue();
        }

        [Fact]
        public void WhenSetAlongsideContinueFrom_ReturnsTrue() {
            var attr = new PolicyContractAttribute("SendWelcomeEmail", version: 2,
                continueFrom: "Legacy.SendWelcomeEmail@v5", startFromEnd: true);

            attr.StartFromEnd.Should().BeTrue();
        }
    }

    public class OnError {
        [Fact]
        public void DefaultsToPark() {
            var attr = new PolicyContractAttribute("SendWelcomeEmail");

            attr.OnError.Should().Be(Aggregates.Subscriptions.SubscriptionErrorMode.Park);
        }

        [Fact]
        public void WhenSetToBlock_ReturnsBlock() {
            var attr = new PolicyContractAttribute("SendWelcomeEmail", onError: Aggregates.Subscriptions.SubscriptionErrorMode.Block);

            attr.OnError.Should().Be(Aggregates.Subscriptions.SubscriptionErrorMode.Block);
        }
    }
}
