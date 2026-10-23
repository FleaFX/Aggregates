using FluentAssertions;
using Microsoft.CodeAnalysis.CSharp.Testing;
using Microsoft.CodeAnalysis.Testing;

namespace Aggregates.Analyzers;

/// <summary>
/// Tests for <see cref="EventContractAnalyzer"/>.
/// </summary>
public class EventContractAnalyzerTests {
    const string Stubs = """
        namespace Aggregates {
            [System.AttributeUsage(System.AttributeTargets.Class | System.AttributeTargets.Struct, Inherited = false)]
            public sealed class EventContractAttribute(string name) : System.Attribute;
            public interface ICommand<in TState, out TEvent>;
        }
        namespace Aggregates.Sagas {
            public interface ISaga<in TSagaState, in TEvent>;
            public interface ISaga<in TSagaState, in TEvent, in TDeadline> : ISaga<TSagaState, TEvent>;
        }
        namespace Aggregates.Policies {
            public interface IPolicy<in TEvent>;
        }
        namespace Aggregates.Projections {
            public interface IProjection<in TEvent>;
        }
        """;

    const string Contracts = """
        namespace Contracts {
            public interface IOrderEvent;
            public class ItemAdded : IOrderEvent;
            [Aggregates.EventContract("ItemRemoved")]
            public class ItemRemoved : IOrderEvent;
            public class OrderPlaced;
        }
        """;

    [Fact]
    public async Task Command_ReportsEventWithoutContract() {
        await VerifyAsync(
            state => {
                state.Sources.Add(Stubs);
                state.Sources.Add("""
                    using Aggregates;

                    namespace Shop {
                        interface IOrderEvent;
                        class {|#0:ItemAdded|} : IOrderEvent;
                        [EventContract("ItemRemoved")]
                        class ItemRemoved : IOrderEvent;
                        class OrderDto;
                        class OrderState;
                        class PlaceOrder : ICommand<OrderState, IOrderEvent>;
                    }
                    """);
            },
            new DiagnosticResult(EventContractAnalyzer.Rule).WithLocation(0).WithArguments("Shop.ItemAdded", ""));
    }

    [Fact]
    public async Task Saga_ReportsEventWithoutContract() {
        await VerifyAsync(
            state => {
                state.Sources.Add(Stubs);
                state.Sources.Add("""
                    using Aggregates.Sagas;

                    namespace Shop {
                        interface IOrderEvent;
                        class {|#0:ItemAdded|} : IOrderEvent;
                        class SagaState;
                        class OrderSaga : ISaga<SagaState, IOrderEvent>;
                    }
                    """);
            },
            new DiagnosticResult(EventContractAnalyzer.Rule).WithLocation(0).WithArguments("Shop.ItemAdded", ""));
    }

    [Fact]
    public async Task Saga_IgnoresTheDeadlineType() {
        await VerifyAsync(
            state => {
                state.Sources.Add(Stubs);
                state.Sources.Add("""
                    using Aggregates.Sagas;

                    namespace Shop {
                        interface IOrderEvent;
                        class {|#0:ItemAdded|} : IOrderEvent;
                        class SagaState;
                        class Timeout;
                        class OrderSaga : ISaga<SagaState, IOrderEvent, Timeout>;
                    }
                    """);
            },
            new DiagnosticResult(EventContractAnalyzer.Rule).WithLocation(0).WithArguments("Shop.ItemAdded", ""));
    }

    [Fact]
    public async Task Policy_ReportsEventWithoutContract() {
        await VerifyAsync(
            state => {
                state.Sources.Add(Stubs);
                state.Sources.Add("""
                    using Aggregates.Policies;

                    namespace Shop {
                        interface IOrderEvent;
                        class {|#0:ItemAdded|} : IOrderEvent;
                        class OrderPolicy : IPolicy<IOrderEvent>;
                    }
                    """);
            },
            new DiagnosticResult(EventContractAnalyzer.Rule).WithLocation(0).WithArguments("Shop.ItemAdded", ""));
    }

    [Fact]
    public async Task Projection_ReportsEventWithoutContract() {
        await VerifyAsync(
            state => {
                state.Sources.Add(Stubs);
                state.Sources.Add("""
                    using Aggregates.Projections;

                    namespace Shop {
                        interface IOrderEvent;
                        class {|#0:ItemAdded|} : IOrderEvent;
                        class OrderProjection : IProjection<IOrderEvent>;
                    }
                    """);
            },
            new DiagnosticResult(EventContractAnalyzer.Rule).WithLocation(0).WithArguments("Shop.ItemAdded", ""));
    }

    [Fact]
    public async Task ObjectEventType_ReportsNothing() {
        await VerifyAsync(state => {
            state.Sources.Add(Stubs);
            state.Sources.Add("""
                using Aggregates.Projections;

                namespace Shop {
                    class Anything;
                    class EverythingProjection : IProjection<object>;
                }
                """);
        });
    }

    [Fact]
    public async Task ConcreteEventType_ReportsTheEvent() {
        await VerifyAsync(
            state => {
                state.Sources.Add(Stubs);
                state.Sources.Add("""
                    using Aggregates.Projections;

                    namespace Shop {
                        class {|#0:OrderPlaced|};
                        class OrderProjection : IProjection<OrderPlaced>;
                    }
                    """);
            },
            new DiagnosticResult(EventContractAnalyzer.Rule).WithLocation(0).WithArguments("Shop.OrderPlaced", ""));
    }

    [Fact]
    public async Task AbstractEventBase_ReportsOnlyTheConcreteEvent() {
        await VerifyAsync(
            state => {
                state.Sources.Add(Stubs);
                state.Sources.Add("""
                    using Aggregates;

                    namespace Shop {
                        interface IOrderEvent;
                        abstract class OrderEvent;
                        interface ISpecialEvent : IOrderEvent;
                        sealed class {|#0:Placed|} : OrderEvent;
                        class OrderState;
                        class PlaceOrder : ICommand<OrderState, OrderEvent>;
                    }
                    """);
            },
            new DiagnosticResult(EventContractAnalyzer.Rule).WithLocation(0).WithArguments("Shop.Placed", ""));
    }

    [Fact]
    public async Task AttributeIsNotInherited_ReportsTheDerivedEvent() {
        await VerifyAsync(
            state => {
                state.Sources.Add(Stubs);
                state.Sources.Add("""
                    using Aggregates;

                    namespace Shop {
                        interface IOrderEvent;
                        [EventContract("Placed")]
                        class Placed : IOrderEvent;
                        class {|#0:SpecialPlaced|} : Placed;
                        class OrderState;
                        class PlaceOrder : ICommand<OrderState, IOrderEvent>;
                    }
                    """);
            },
            new DiagnosticResult(EventContractAnalyzer.Rule).WithLocation(0).WithArguments("Shop.SpecialPlaced", ""));
    }

    [Fact]
    public async Task RecordStructEvent_ReportsTheEvent() {
        await VerifyAsync(
            state => {
                state.Sources.Add(Stubs);
                state.Sources.Add("""
                    using Aggregates.Projections;

                    namespace Shop {
                        interface IOrderEvent;
                        record struct {|#0:Moved|}(int X) : IOrderEvent;
                        class OrderProjection : IProjection<IOrderEvent>;
                    }
                    """);
            },
            new DiagnosticResult(EventContractAnalyzer.Rule).WithLocation(0).WithArguments("Shop.Moved", ""));
    }

    [Fact]
    public async Task OpenGenericEvent_ReportsTheEvent() {
        await VerifyAsync(
            state => {
                state.Sources.Add(Stubs);
                state.Sources.Add("""
                    using Aggregates;

                    namespace Shop {
                        interface IOrderEvent;
                        class {|#0:Wrapped|}<T> : IOrderEvent;
                        class OrderState;
                        class PlaceOrder : ICommand<OrderState, IOrderEvent>;
                    }
                    """);
            },
            new DiagnosticResult(EventContractAnalyzer.Rule).WithLocation(0).WithArguments("Shop.Wrapped<T>", ""));
    }

    [Fact]
    public async Task EventOfTwoHandlers_ReportsOnce() {
        await VerifyAsync(
            state => {
                state.Sources.Add(Stubs);
                state.Sources.Add("""
                    using Aggregates;
                    using Aggregates.Projections;

                    namespace Shop {
                        interface IOrderEvent;
                        class {|#0:ItemAdded|} : IOrderEvent;
                        class OrderState;
                        class PlaceOrder : ICommand<OrderState, IOrderEvent>;
                        class OrderProjection : IProjection<IOrderEvent>;
                    }
                    """);
            },
            new DiagnosticResult(EventContractAnalyzer.Rule).WithLocation(0).WithArguments("Shop.ItemAdded", ""));
    }

    [Fact]
    public async Task GenericHandler_ReportsNothing() {
        await VerifyAsync(state => {
            state.Sources.Add(Stubs);
            state.Sources.Add("""
                using Aggregates.Projections;

                namespace Shop {
                    class Anything;
                    class AnyProjection<T> : IProjection<T>;
                }
                """);
        });
    }

    [Fact]
    public async Task WithoutAggregates_ReportsNothing() {
        await VerifyAsync(state => {
            state.Sources.Add("""
                namespace Other {
                    public interface ICommand<S, E>;
                    public interface IEvent;
                    public class Added : IEvent;
                    public class Place : ICommand<object, IEvent>;
                }
                """);
        });
    }

    [Fact]
    public async Task ReferencedAssembly_ReportsCommandEvent() {
        await VerifyAsync(
            state => {
                AddReferencedContracts(state);
                state.Sources.Add("""
                    using Aggregates;

                    namespace Shop {
                        class OrderState;
                        class {|#0:PlaceOrder|} : ICommand<OrderState, Contracts.IOrderEvent>;
                    }
                    """);
            },
            new DiagnosticResult(EventContractAnalyzer.Rule).WithLocation(0).WithArguments("Contracts.ItemAdded", " in assembly 'Contracts'"));
    }

    [Fact]
    public async Task ReferencedAssembly_ReportsConcreteCommandEvent() {
        await VerifyAsync(
            state => {
                AddReferencedContracts(state);
                state.Sources.Add("""
                    using Aggregates;

                    namespace Shop {
                        class OrderState;
                        class {|#0:Place|} : ICommand<OrderState, Contracts.OrderPlaced>;
                    }
                    """);
            },
            new DiagnosticResult(EventContractAnalyzer.Rule).WithLocation(0).WithArguments("Contracts.OrderPlaced", " in assembly 'Contracts'"));
    }

    [Fact]
    public async Task ReferencedAssembly_IgnoresSagaPolicyAndProjection() {
        await VerifyAsync(state => {
            AddReferencedContracts(state);
            state.Sources.Add("""
                using Aggregates.Policies;
                using Aggregates.Projections;
                using Aggregates.Sagas;

                namespace Shop {
                    class SagaState;
                    class OrderSaga : ISaga<SagaState, Contracts.IOrderEvent>;
                    class OrderPolicy : IPolicy<Contracts.IOrderEvent>;
                    class OrderProjection : IProjection<Contracts.IOrderEvent>;
                }
                """);
        });
    }

    [Fact]
    public async Task ReferencedAssembly_ReportsEventOnce() {
        await VerifyAsync(
            state => {
                AddReferencedContracts(state);
                state.Sources.Add(("B.cs", """
                    using Aggregates;

                    namespace Shop {
                        class OtherState;
                        class Second : ICommand<OtherState, Contracts.IOrderEvent>;
                    }
                    """));
                state.Sources.Add(("A.cs", """
                    using Aggregates;

                    namespace Shop {
                        class OrderState;
                        class {|#0:First|} : ICommand<OrderState, Contracts.IOrderEvent>;
                    }
                    """));
            },
            new DiagnosticResult(EventContractAnalyzer.Rule).WithLocation(0).WithArguments("Contracts.ItemAdded", " in assembly 'Contracts'"));
    }

    [Fact]
    public void KnownTypes_MatchTheMetadataNamesOfTheRealTypes() {
        KnownTypes.EventContractAttribute.Should().Be(typeof(Aggregates.EventContractAttribute).FullName);
        KnownTypes.Command.Should().Be(typeof(Aggregates.ICommand<,>).FullName);
        KnownTypes.Saga.Should().Be(typeof(Aggregates.Sagas.ISaga<,>).FullName);
        KnownTypes.Policy.Should().Be(typeof(Aggregates.Policies.IPolicy<>).FullName);
        KnownTypes.Projection.Should().Be(typeof(Aggregates.Projections.IProjection<>).FullName);
    }

    static void AddReferencedContracts(SolutionState state) {
        state.AdditionalProjects["Aggregates"].Sources.Add(Stubs);
        state.AdditionalProjects["Contracts"].Sources.Add(Contracts);
        state.AdditionalProjects["Contracts"].AdditionalProjectReferences.Add("Aggregates");
        state.AdditionalProjectReferences.Add("Aggregates");
        state.AdditionalProjectReferences.Add("Contracts");
    }

    static Task VerifyAsync(Action<SolutionState> arrange, params DiagnosticResult[] expected) {
        var test = new CSharpAnalyzerTest<EventContractAnalyzer, DefaultVerifier> {
            ReferenceAssemblies = ReferenceAssemblies.Net.Net80,
        };
        arrange(test.TestState);
        test.ExpectedDiagnostics.AddRange(expected);
        return test.RunAsync(TestContext.Current.CancellationToken);
    }
}
