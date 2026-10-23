using System.Reflection;
using System.Reflection.Emit;
using FluentAssertions;

namespace Aggregates;

public interface IShipmentEvent;

/// A shipment event, stored under its current contract name.
[EventContract("Shipped", 2, @namespace: "Tests.Registry")]
public sealed record Shipped(string OrderId) : IShipmentEvent;

/// The first version of <see cref="Shipped"/>, stored under another name, which upgrades to it.
[EventContract("ShippedV1", @namespace: "Tests.Registry", eventUpgrader: typeof(ShippedV1ToShipped))]
public sealed record ShippedV1(string OrderId);

/// Upgrades a <see cref="ShippedV1"/> to a <see cref="Shipped"/>.
public sealed class ShippedV1ToShipped : IUpgradeEvent<ShippedV1, Shipped> {
    public Shipped Upgrade(ShippedV1 @event) => new(@event.OrderId);
}

/// The first version of Counted, whose upgrader counts each time it is created.
[EventContract("Counted", @namespace: "Tests.Registry", eventUpgrader: typeof(CountedV1ToShipped))]
public sealed record CountedV1(string OrderId);

/// <summary>
/// Upgrades a <see cref="CountedV1"/> to a <see cref="Shipped"/>, and counts each creation.
/// </summary>
public sealed class CountedV1ToShipped : IUpgradeEvent<CountedV1, Shipped> {
    public static int Created;

    public CountedV1ToShipped() => Interlocked.Increment(ref Created);

    public Shipped Upgrade(CountedV1 @event) => new(@event.OrderId);
}

/// The first version of Dependent, whose upgrader has no parameterless constructor.
[EventContract("Dependent", @namespace: "Tests.Registry", eventUpgrader: typeof(DependentV1ToShipped))]
public sealed record DependentV1(string OrderId);

/// <summary>
/// Upgrades a <see cref="DependentV1"/> to a <see cref="Shipped"/>; has no parameterless constructor.
/// </summary>
public sealed class DependentV1ToShipped(string prefix) : IUpgradeEvent<DependentV1, Shipped> {
    public Shipped Upgrade(DependentV1 @event) => new(prefix + @event.OrderId);
}

/// An event type whose upgrader upgrades two types, so the first upgrade it offers may not be its own.
[EventContract("Twice", @namespace: "Tests.Registry", eventUpgrader: typeof(TwiceUpgrader))]
public sealed record TwiceV1(string OrderId);

/// Upgrades a <see cref="TwiceV1"/> and a <see cref="ShippedV1"/> to a <see cref="Shipped"/>.
public sealed class TwiceUpgrader : IUpgradeEvent<TwiceV1, Shipped>, IUpgradeEvent<ShippedV1, Shipped> {
    public Shipped Upgrade(TwiceV1 @event) => new(@event.OrderId);
    public Shipped Upgrade(ShippedV1 @event) => new(@event.OrderId);
}

/// An event type that no shipment handler receives.
[EventContract("Other", @namespace: "Tests.Registry")]
public sealed record OtherEvent;

/// A shipment event without an <see cref="EventContractAttribute"/>, stored under its short CLR name.
public sealed record Packed(string OrderId) : IShipmentEvent;

/// The first version of Delivered, whose upgrader wrongly sits on the current version.
[EventContract("Delivered", 1, @namespace: "Tests.Registry")]
public sealed record DeliveredV1(string OrderId);

/// The current version of Delivered.
[EventContract("Delivered", 2, @namespace: "Tests.Registry", eventUpgrader: typeof(DeliveredV1ToV2))]
public sealed record DeliveredV2(string OrderId) : IShipmentEvent;

/// Upgrades a <see cref="DeliveredV1"/> to a <see cref="DeliveredV2"/>.
public sealed class DeliveredV1ToV2 : IUpgradeEvent<DeliveredV1, DeliveredV2> {
    public DeliveredV2 Upgrade(DeliveredV1 @event) => new(@event.OrderId);
}

/// The first version of Cancelled.
[EventContract("Cancelled", 1, @namespace: "Tests.Registry", eventUpgrader: typeof(CancelledV1ToV2))]
public sealed record CancelledV1(string OrderId);

/// The second version of Cancelled.
[EventContract("Cancelled", 2, @namespace: "Tests.Registry", eventUpgrader: typeof(CancelledV2ToV3))]
public sealed record CancelledV2(string OrderId);

/// The current version of Cancelled, the only one a handler can receive directly.
[EventContract("Cancelled", 3, @namespace: "Tests.Registry")]
public sealed record CancelledV3(string OrderId) : IShipmentEvent;

/// Upgrades a <see cref="CancelledV1"/> to a <see cref="CancelledV2"/>.
public sealed class CancelledV1ToV2 : IUpgradeEvent<CancelledV1, CancelledV2> {
    public CancelledV2 Upgrade(CancelledV1 @event) => new(@event.OrderId);
}

/// Upgrades a <see cref="CancelledV2"/> to a <see cref="CancelledV3"/>.
public sealed class CancelledV2ToV3 : IUpgradeEvent<CancelledV2, CancelledV3> {
    public CancelledV3 Upgrade(CancelledV2 @event) => new(@event.OrderId);
}

/// An event type that upgrades to <see cref="CycleB"/> and back, so neither reaches a handler.
[EventContract("CycleA", @namespace: "Tests.Registry", eventUpgrader: typeof(CycleAToB))]
public sealed record CycleA;

/// An event type that upgrades to <see cref="CycleA"/> and back, so neither reaches a handler.
[EventContract("CycleB", @namespace: "Tests.Registry", eventUpgrader: typeof(CycleBToA))]
public sealed record CycleB;

/// Upgrades a <see cref="CycleA"/> to a <see cref="CycleB"/>.
public sealed class CycleAToB : IUpgradeEvent<CycleA, CycleB> {
    public CycleB Upgrade(CycleA @event) => new();
}

/// Upgrades a <see cref="CycleB"/> to a <see cref="CycleA"/>.
public sealed class CycleBToA : IUpgradeEvent<CycleB, CycleA> {
    public CycleA Upgrade(CycleB @event) => new();
}

/// An abstract shipment event, which is never stored.
abstract record AbstractShipment(string OrderId) : IShipmentEvent;

/// An open generic shipment event, which is never stored.
sealed class GenericShipment<T> : IShipmentEvent { }

/// A generic event type whose contract is shared by every closing, which a registry refuses.
[EventContract("Envelope", @namespace: "Tests.Registry")]
public sealed record Envelope<T>(T Value);

/// An envelope without a contract, which a registry refuses once closed over its type argument.
public sealed record SomeEnvelope<T>(T Value);

/// An event whose upgrader always throws.
[EventContract("Broken", @namespace: "Tests.Registry", eventUpgrader: typeof(BrokenToShipped))]
public sealed record BrokenEvent(string OrderId);

/// <summary>
/// Upgrades a <see cref="BrokenEvent"/> to a <see cref="Shipped"/>, and always throws.
/// </summary>
public sealed class BrokenToShipped : IUpgradeEvent<BrokenEvent, Shipped> {
    public Shipped Upgrade(BrokenEvent @event) => throw new FormatException("broken on purpose");
}

public class EventTypeRegistryTests {
    public class EventContract {
        [Fact]
        public void EventUpgrader_ReturnsTheConfiguredType() {
            var attribute = new EventContractAttribute("Shipped", 2, typeof(ShippedV1ToShipped), "Tests.Registry");

            attribute.EventUpgrader.Should().Be(typeof(ShippedV1ToShipped));
        }

        [Fact]
        public void WithoutUpgrader_EventUpgraderIsNull() {
            var attribute = new EventContractAttribute("Shipped", 2, @namespace: "Tests.Registry");

            attribute.EventUpgrader.Should().BeNull();
        }

        [Fact]
        public void Upgrade_OfNull_ReturnsNull() {
            EventContractAttribute.Upgrade(null).Should().BeNull();
        }

        [Fact]
        public void Upgrade_WithoutUpgrader_ReturnsTheSameInstance() {
            var @event = new Shipped("o");

            EventContractAttribute.Upgrade(@event).Should().BeSameAs(@event);
        }

        [Fact]
        public void Upgrade_FollowsTheChainToTheLastVersion() {
            EventContractAttribute.Upgrade(new CancelledV1("o")).Should().Be(new CancelledV3("o"));
        }

        [Fact]
        public void Upgrade_StartsAtTheVersionOfTheEvent() {
            EventContractAttribute.Upgrade(new CancelledV2("o")).Should().Be(new CancelledV3("o"));
        }

        [Fact]
        public void Upgrade_CreatesTheUpgraderOnFirstUse() {
            var before = CountedV1ToShipped.Created;
            typeof(CountedV1).GetCustomAttribute<EventContractAttribute>().Should().NotBeNull();
            CountedV1ToShipped.Created.Should().Be(before);
            EventContractAttribute.Upgrade(new CountedV1("o-1")).Should().Be(new Shipped("o-1"));
            CountedV1ToShipped.Created.Should().Be(before + 1);
        }

        [Fact]
        public void ReadingTheAttribute_DoesNotThrow_ForAnUpgraderWithoutParameterlessConstructor() {
            var attribute = typeof(DependentV1).GetCustomAttribute<EventContractAttribute>();

            attribute!.EventUpgrader.Should().Be(typeof(DependentV1ToShipped));
        }

        [Fact]
        public void GivenAWhitespaceName_ThrowsArgumentException() {
            var act = () => new EventContractAttribute("  ", @namespace: "Tests.Registry");

            act.Should().Throw<ArgumentException>().Which.Should().NotBeOfType<ArgumentNullException>();
        }

        [Fact]
        public void GivenVersionZero_Throws() {
            var act = () => new EventContractAttribute("Shipped", version: 0, @namespace: "Tests.Registry");

            act.Should().Throw<ArgumentOutOfRangeException>();
        }

        [Fact]
        public void Upgrade_LetsTheUpgradersExceptionThrough() {
            var act = () => EventContractAttribute.Upgrade(new BrokenEvent("o-1"));

            act.Should().Throw<FormatException>();
        }
    }

    public class GetTypeNames {
        [Fact]
        public void GivenAssignableTypes_ReturnsTheirNamesSortedWithoutTheOthers() {
            var registry = EventTypeRegistry.FromTypes([typeof(Shipped), typeof(Packed), typeof(OtherEvent)]);

            registry.GetTypeNames(typeof(IShipmentEvent))
                .Should().Equal(typeof(Packed).FullName, "Tests.Registry.Shipped@v2");
        }

        [Fact]
        public void GivenTwoTypesWithTheSameShortName_ReturnsBothFullNames() {
            var registry = EventTypeRegistry.FromTypes([typeof(One.Duplicate), typeof(Two.Duplicate)]);

            registry.GetTypeNames(typeof(IShipmentEvent))
                .Should().Equal(typeof(One.Duplicate).FullName, typeof(Two.Duplicate).FullName);
        }

        [Fact]
        public void GivenAnUpgraderOnTheOldType_IncludesTheOldVersion() {
            var registry = EventTypeRegistry.FromTypes([typeof(Shipped), typeof(ShippedV1)]);

            registry.GetTypeNames(typeof(IShipmentEvent))
                .Should().Equal("Tests.Registry.Shipped@v2", "Tests.Registry.ShippedV1@v1");
        }

        [Fact]
        public void GivenAnUpgradeChain_IncludesEveryVersion() {
            var registry = EventTypeRegistry.FromTypes([typeof(CancelledV1), typeof(CancelledV2), typeof(CancelledV3)]);

            registry.GetTypeNames(typeof(IShipmentEvent)).Should().Equal(
                "Tests.Registry.Cancelled@v1", "Tests.Registry.Cancelled@v2", "Tests.Registry.Cancelled@v3");
        }

        [Fact]
        public void GivenAnUpgradeCycle_LeavesItOutWithoutLooping() {
            var registry = EventTypeRegistry.FromTypes([typeof(CycleA), typeof(CycleB)]);

            registry.GetTypeNames(typeof(IShipmentEvent)).Should().BeEmpty();
        }

        [Fact]
        public void GivenATypeWithoutAnEventContract_ReturnsItsFullName() {
            var registry = EventTypeRegistry.FromTypes([typeof(Packed)]);

            registry.GetTypeNames(typeof(IShipmentEvent)).Should().Equal(typeof(Packed).FullName);
            registry.TryGetType("Packed", out _).Should().BeFalse();
        }

        [Fact]
        public void TryGetType_FindsATypeWithoutContract_ByItsFullName() {
            var registry = EventTypeRegistry.FromTypes([typeof(Packed)]);

            registry.TryGetType(registry.GetTypeName(typeof(Packed)), out var type).Should().BeTrue();
            type.Should().Be(typeof(Packed));
        }

        [Fact]
        public void GivenAbstractInterfacesAndOpenGenerics_ReturnsNoNames() {
            var registry = EventTypeRegistry.FromTypes(
                [typeof(AbstractShipment), typeof(IShipmentEvent), typeof(GenericShipment<>)]);

            registry.GetTypeNames(typeof(object)).Should().BeEmpty();
        }

        [Fact]
        public void GivenNoMatchingType_ReturnsNoNames() {
            var registry = EventTypeRegistry.FromTypes([typeof(OtherEvent)]);

            registry.GetTypeNames(typeof(IShipmentEvent)).Should().BeEmpty();
        }

        static class One { public sealed record Duplicate(string OrderId) : IShipmentEvent; }
        static class Two { public sealed record Duplicate(string OrderId) : IShipmentEvent; }
    }

    public class GetTypeName {
        [Fact]
        public void OfAnUnscannedTypeWithContract_ReturnsTheContractName() {
            var registry = EventTypeRegistry.FromTypes([typeof(Shipped)]);

            registry.GetTypeName(typeof(ShippedV1)).Should().Be("Tests.Registry.ShippedV1@v1");
        }

        [Fact]
        public void OfAnUnscannedGenericTypeWithoutContract_Throws() {
            var registry = EventTypeRegistry.FromTypes([typeof(Shipped)]);

            var act = () => registry.GetTypeName(typeof(SomeEnvelope<int>));

            act.Should().Throw<InvalidOperationException>();
        }
    }

    public class Validation {
        [Fact]
        public void GivenAnUpgraderOnTheNewType_Throws() {
            var act = () => EventTypeRegistry.FromTypes([typeof(DeliveredV1), typeof(DeliveredV2)]);

            act.Should().Throw<InvalidOperationException>().Which.Message.Should()
                .Contain("Aggregates.DeliveredV2: eventUpgrader DeliveredV1ToV2 implements IUpgradeEvent<DeliveredV1, DeliveredV2>");
        }

        [Fact]
        public void GivenUpgradersOnTheOldTypes_DoesNotThrow() {
            var act = () => EventTypeRegistry.FromTypes(
                [typeof(Shipped), typeof(ShippedV1), typeof(CancelledV1), typeof(CancelledV2), typeof(CancelledV3)]);

            act.Should().NotThrow();
        }

        [Fact]
        public void GivenAnUpgraderWithTwoUpgrades_Throws() {
            var act = () => EventTypeRegistry.FromTypes([typeof(TwiceV1)]);

            var message = act.Should().Throw<InvalidOperationException>().Which.Message;
            message.Should().Contain("Aggregates.TwiceV1: eventUpgrader TwiceUpgrader implements IUpgradeEvent<");
            message.Should().Contain("IUpgradeEvent<TwiceV1, Shipped>");
            message.Should().Contain("IUpgradeEvent<ShippedV1, Shipped>");
        }

        [Fact]
        public void GivenSeveralMisconfiguredTypes_ReportsEveryOne() {
            var act = () => EventTypeRegistry.FromTypes([typeof(DeliveredV1), typeof(DeliveredV2), typeof(TwiceV1)]);

            var message = act.Should().Throw<InvalidOperationException>().Which.Message;
            message.Should().Contain("Aggregates.DeliveredV2: eventUpgrader DeliveredV1ToV2 implements IUpgradeEvent<DeliveredV1, DeliveredV2>");
            message.Should().Contain("Aggregates.TwiceV1: eventUpgrader TwiceUpgrader");
            message.Should().NotContain("DeliveredV1: eventUpgrader");
        }

        [Fact]
        public void GivenAnUpgraderWithoutParameterlessConstructor_Throws() {
            var act = () => EventTypeRegistry.FromTypes([typeof(DependentV1), typeof(Shipped)]);

            act.Should().Throw<InvalidOperationException>().Which.Message.Should()
                .Contain("Aggregates.DependentV1: eventUpgrader DependentV1ToShipped has no public parameterless constructor.");
        }

        [Fact]
        public void GivenAnUpgradeCycle_DoesNotThrow() {
            var act = () => EventTypeRegistry.FromTypes([typeof(CycleA), typeof(CycleB)]);

            act.Should().NotThrow();
        }

        [Fact]
        public void GivenTwoTypesWithTheSameContract_Throws() {
            var act = () => EventTypeRegistry.FromTypes([typeof(Envelope<int>), typeof(Envelope<string>)]);

            var message = act.Should().Throw<InvalidOperationException>().Which.Message;
            message.Should().Contain("Tests.Registry.Envelope@v1");
            message.Should().Contain(typeof(Envelope<int>).FullName);
            message.Should().Contain(typeof(Envelope<string>).FullName);
        }

        [Fact]
        public void GivenAGenericTypeWithoutContract_Throws() {
            var act = () => EventTypeRegistry.FromTypes([typeof(SomeEnvelope<int>)]);

            act.Should().Throw<InvalidOperationException>().Which.Message.Should()
                .Contain("a generic event type needs an EventContractAttribute");
        }

        [Fact]
        public void GivenTwoTypesWithoutContractWithTheSameFullName_LeavesTheNameOut() {
            var first = DynamicType("FirstDynamic", "Shared.Name");
            var second = DynamicType("SecondDynamic", "Shared.Name");

            var registry = EventTypeRegistry.FromTypes(first, second);

            registry.TryGetType("Shared.Name", out _).Should().BeFalse();
            registry.GetTypeName(first).Should().Be("Shared.Name");
        }

        // Two types with the same full name, like generated classes in two assemblies.
        static Type DynamicType(string assembly, string fullName) =>
            AssemblyBuilder.DefineDynamicAssembly(new AssemblyName(assembly), AssemblyBuilderAccess.Run)
                .DefineDynamicModule(assembly)
                .DefineType(fullName, TypeAttributes.Public | TypeAttributes.Sealed)
                .CreateType();

        [Fact]
        public void FromAssemblies_ChecksTheScannedTypes() {
            var act = () => EventTypeRegistry.FromAssemblies(typeof(Shipped).Assembly);

            var message = act.Should().Throw<InvalidOperationException>().Which.Message;
            message.Should().Contain("Aggregates.DeliveredV2: eventUpgrader DeliveredV1ToV2 implements IUpgradeEvent<DeliveredV1, DeliveredV2>");
            message.Should().Contain("Aggregates.TwiceV1: eventUpgrader TwiceUpgrader");
        }
    }
}
