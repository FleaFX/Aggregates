using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;

namespace Aggregates.Subscriptions;

public class SubscriptionRegistrationsTests {
    public class FindHandlers {
        [Fact]
        public void GivenOneImplementation_ReturnsTheClassWithItsInterface() {
            var handlers = SubscriptionRegistrations.FindHandlers([typeof(SingleHandler)], typeof(IHandles<>));

            handlers.Should().Equal((typeof(SingleHandler), typeof(IHandles<IFirstEvent>)));
        }

        [Fact]
        public void IgnoresTypesWithoutTheInterface() {
            var handlers = SubscriptionRegistrations.FindHandlers([typeof(string), typeof(IFirstEvent)], typeof(IHandles<>));

            handlers.Should().BeEmpty();
        }

        [Fact]
        public void IgnoresAbstractAndOpenGenericTypes() {
            var handlers = SubscriptionRegistrations.FindHandlers(
                [typeof(AbstractHandler), typeof(GenericHandler<>), typeof(IHandles<IFirstEvent>)], typeof(IHandles<>));

            handlers.Should().BeEmpty();
        }

        [Fact]
        public void GivenTheSameTypeTwice_ReturnsItOnce() {
            var handlers = SubscriptionRegistrations.FindHandlers([typeof(SingleHandler), typeof(SingleHandler)], typeof(IHandles<>));

            handlers.Should().ContainSingle();
        }

        [Fact]
        public void GivenTwoImplementations_Throws_WithTheClassAndAHint() {
            var find = () => SubscriptionRegistrations.FindHandlers([typeof(DoubleHandler)], typeof(IHandles<>));

            find.Should().Throw<InvalidOperationException>()
                .WithMessage($"*{typeof(DoubleHandler).FullName}*IHandles<IFirstEvent>*IHandles<ISecondEvent>*IHandles<TEvent> once*marker interface*");
        }

        [Fact]
        public void CountsInheritedImplementations() {
            var find = () => SubscriptionRegistrations.FindHandlers([typeof(DerivedHandler)], typeof(IHandles<>));

            find.Should().Throw<InvalidOperationException>().WithMessage($"*{typeof(DerivedHandler).FullName}*");
        }
    }

    public class GetSubscriptionId {
        [Fact]
        public void GivenAContract_ReturnsItsStringForm() =>
            SubscriptionRegistrations.GetSubscriptionId(typeof(SingleHandler), new TestContractAttribute("Orders.Summary@v2"))
                .Should().Be("Orders.Summary@v2");

        [Fact]
        public void GivenNoContract_ReturnsTheFullTypeName() =>
            SubscriptionRegistrations.GetSubscriptionId(typeof(SingleHandler), null)
                .Should().Be("Aggregates.Subscriptions.SubscriptionRegistrationsTests+SingleHandler");
    }

    public class Add {
        readonly ServiceCollection _services = new();

        static SubscriptionRegistration Registration(string id, Type handlerType, params string[] predecessors) =>
            new(id, handlerType, typeof(IFirstEvent), StartFromEnd: false,
                Predecessors: predecessors.Length == 0 ? null : predecessors);

        [Fact]
        public void AddsTheRegistrationAsSingletonInstance() {
            var registration = Registration("Orders@v1", typeof(SingleHandler));

            SubscriptionRegistrations.Add(_services, registration).Should().BeTrue();

            using var provider = _services.BuildServiceProvider();
            provider.GetServices<SubscriptionRegistration>().Should().Equal(registration);
        }

        [Fact]
        public void GivenTheSameHandlerUnderTheSameId_AddsNothing() {
            SubscriptionRegistrations.Add(_services, Registration("Orders@v1", typeof(SingleHandler)));

            SubscriptionRegistrations.Add(_services, Registration("Orders@v1", typeof(SingleHandler))).Should().BeFalse();

            _services.Should().ContainSingle(d => d.ServiceType == typeof(SubscriptionRegistration));
        }

        [Fact]
        public void GivenAnotherHandlerUnderTheSameId_Throws_NamingBoth() {
            SubscriptionRegistrations.Add(_services, Registration("Orders@v1", typeof(SingleHandler)));

            var add = () => SubscriptionRegistrations.Add(_services, Registration("Orders@v1", typeof(OtherHandler)));

            add.Should().Throw<InvalidOperationException>()
                .WithMessage($"*'Orders@v1'*{typeof(SingleHandler).FullName}*{typeof(OtherHandler).FullName}*");
        }

        [Fact]
        public void ComparesIdsCaseSensitively() {
            SubscriptionRegistrations.Add(_services, Registration("Orders@v1", typeof(SingleHandler)));

            SubscriptionRegistrations.Add(_services, Registration("orders@v1", typeof(OtherHandler))).Should().BeTrue();
        }

        [Fact]
        public void GivenDifferentIds_AddsBoth() {
            SubscriptionRegistrations.Add(_services, Registration("Orders@v1", typeof(SingleHandler)));

            SubscriptionRegistrations.Add(_services, Registration("Orders@v2", typeof(OtherHandler))).Should().BeTrue();

            _services.Count(d => d.ServiceType == typeof(SubscriptionRegistration)).Should().Be(2);
        }

        [Fact]
        public void GivenARegistrationThatContinuesFromItself_Throws() {
            var add = () => SubscriptionRegistrations.Add(_services, Registration("Orders@v2", typeof(SingleHandler), "Orders@v2"));

            add.Should().Throw<InvalidOperationException>()
                .WithMessage("*'Orders@v2'*continues from itself*");
        }

        [Fact]
        public void GivenAPredecessorAlreadyRegistered_Throws() {
            SubscriptionRegistrations.Add(_services, Registration("Orders@v1", typeof(SingleHandler)));

            var add = () => SubscriptionRegistrations.Add(_services, Registration("Orders@v2", typeof(OtherHandler), "Orders@v1"));

            add.Should().Throw<InvalidOperationException>()
                .WithMessage($"*'Orders@v2'*'Orders@v1'*{typeof(SingleHandler).FullName}*");
        }

        [Fact]
        public void GivenASuccessorAlreadyRegistered_Throws() {
            SubscriptionRegistrations.Add(_services, Registration("Orders@v2", typeof(OtherHandler), "Orders@v1"));

            var add = () => SubscriptionRegistrations.Add(_services, Registration("Orders@v1", typeof(SingleHandler)));

            add.Should().Throw<InvalidOperationException>()
                .WithMessage($"*'Orders@v2'*'Orders@v1'*{typeof(SingleHandler).FullName}*");
        }

        [Fact]
        public void GivenAPredecessorTwoLevelsDown_Throws() {
            SubscriptionRegistrations.Add(_services, Registration("Orders@v1", typeof(SingleHandler)));

            var add = () => SubscriptionRegistrations.Add(_services,
                Registration("Orders@v3", typeof(OtherHandler), "Orders@v2", "Orders@v1"));

            add.Should().Throw<InvalidOperationException>()
                .WithMessage($"*'Orders@v3'*'Orders@v1'*{typeof(SingleHandler).FullName}*");
        }

        [Fact]
        public void GivenUnrelatedPredecessors_AddsTheRegistration() {
            SubscriptionRegistrations.Add(_services, Registration("Orders@v1", typeof(SingleHandler)));

            SubscriptionRegistrations.Add(_services, Registration("Orders@v2", typeof(OtherHandler), "Legacy.Orders@v1"))
                .Should().BeTrue();
        }
    }

    interface IHandles<TEvent>;

    interface IFirstEvent;

    interface ISecondEvent;

    sealed class SingleHandler : IHandles<IFirstEvent>;

    sealed class OtherHandler : IHandles<IFirstEvent>;

    sealed class DoubleHandler : IHandles<IFirstEvent>, IHandles<ISecondEvent>;

    class BaseHandler : IHandles<IFirstEvent>;

    sealed class DerivedHandler : BaseHandler, IHandles<ISecondEvent>;

    abstract class AbstractHandler : IHandles<IFirstEvent>;

    sealed class GenericHandler<TEvent> : IHandles<TEvent>;

    sealed class TestContractAttribute(string id) : Attribute {
        public override string ToString() => id;
    }
}
