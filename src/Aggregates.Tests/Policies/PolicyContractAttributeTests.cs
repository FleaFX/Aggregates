using FluentAssertions;

namespace Aggregates.Policies;

public class PolicyContractAttributeTests {

    public class ToString {
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
    }
}
