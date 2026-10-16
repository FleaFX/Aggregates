using System.Runtime.CompilerServices;
using Aggregates.Policies;
using Aggregates.Subscriptions;
using Aggregates.Testing;
using FakeItEasy;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace Aggregates.Sagas;

public class ServiceCollectionExtensionsTests {

    public class AddSagas {
        static IEnumerable<SubscriptionRegistration> Registrations(IServiceCollection services) =>
            services.Select(d => d.ImplementationInstance).OfType<SubscriptionRegistration>();

        [Fact]
        public void ScanTypes_RegistersOnlyTheGivenSagas() {
            var services = new ServiceCollection();

            services.AddAggregates().AddSagas(o => o.ScanTypes(typeof(TestSaga)));

            services.Should().ContainSingle(d => d.ServiceType == typeof(TestSaga));
            services.Should().NotContain(d => d.ServiceType == typeof(OtherTestSaga));
            services.Should().ContainSingle(d => d.ServiceType == typeof(IHostedService));
        }

        [Fact]
        public void RegistersTheSagaAsItself_NotAsItsInterface() {
            var services = new ServiceCollection();

            services.AddAggregates().AddSagas(o => o.ScanTypes(typeof(TestSaga)));

            services.Should().NotContain(d => d.ServiceType == typeof(ISaga<TestSagaState, TestEvent>));
        }

        [Fact]
        public void RegistersTheSchedulerAndActivation() {
            var services = new ServiceCollection();

            services.AddAggregates().AddSagas(o => o.ScanTypes(typeof(TestSaga)));

            services.Should().ContainSingle(d => d.ServiceType == typeof(Scheduler));
            services.Should().ContainSingle(d => d.ServiceType == typeof(SchedulerActivation<>));
        }

        [Fact]
        public void ScanTypes_SameTypeTwice_StartsOneSubscription() {
            var services = new ServiceCollection();

            services.AddAggregates().AddSagas(o => o.ScanTypes(typeof(TestSaga), typeof(TestSaga)));

            services.Should().ContainSingle(d => d.ServiceType == typeof(IHostedService));
        }

        [Fact]
        public void CalledTwiceWithTheSameType_StartsOneSubscription() {
            var services = new ServiceCollection();
            var builder = services.AddAggregates();

            builder.AddSagas(o => o.ScanTypes(typeof(TestSaga)));
            builder.AddSagas(o => o.ScanTypes(typeof(TestSaga)));

            services.Should().ContainSingle(d => d.ServiceType == typeof(IHostedService));
            Registrations(services).Should().ContainSingle();
        }

        [Fact]
        public void ScanTypes_IgnoresTypesThatAreNotSagas() {
            var services = new ServiceCollection();

            services.AddAggregates().AddSagas(o => o.ScanTypes(typeof(string)));

            services.Should().NotContain(d => d.ServiceType == typeof(IHostedService));
        }

        // Registering the whole test assembly would fail on DoubleSaga, so this checks what gets scanned.
        [Fact]
        public void ScanAssemblies_InspectsEveryTypeInTheAssembly() {
            var options = new SagasOptions().ScanAssemblies(typeof(TestSaga).Assembly);

            options.Types.Should().Contain([typeof(TestSaga), typeof(OtherTestSaga)]);
        }

        [Fact]
        public void RegistersTheSaga_AndStartsItsSubscription() {
            var services = new ServiceCollection();

            services.AddAggregates().AddSagas(o => o.ScanTypes(typeof(TestSaga)));

            services.Should().ContainSingle(d => d.ServiceType == typeof(TestSaga));
            services.Should().ContainSingle(d => d.ServiceType == typeof(IHostedService));
            Registrations(services).Should().ContainSingle();
        }

        [Theory]
        [InlineData(typeof(PlainContractSaga), "Fulfillment@v1")]
        [InlineData(typeof(FullContractSaga), "Orders.Fulfillment@v2")]
        [InlineData(typeof(TestSaga), "Aggregates.Sagas.TestSaga")]
        public void SubscriptionId_IsTheContract_OrTheFullTypeName(Type sagaType, string expectedId) {
            var services = new ServiceCollection();

            services.AddAggregates().AddSagas(o => o.ScanTypes(sagaType));

            Registrations(services).Should().ContainSingle().Which.SubscriptionId.Should().Be(expectedId);
        }

        [Fact]
        public void Registration_HasTheSagaEventTypeAndStartFromEnd() {
            var services = new ServiceCollection();

            services.AddAggregates().AddSagas(o => o.ScanTypes(typeof(FullContractSaga)));

            Registrations(services).Should().ContainSingle().Which.Should().BeEquivalentTo(
                new SubscriptionRegistration("Orders.Fulfillment@v2", typeof(FullContractSaga), typeof(TestEvent), StartFromEnd: true,
                    Predecessors: ["Orders.Fulfillment@v1"]));
        }

        [Fact]
        public void GivenAVersion1Contract_RegistersNoPredecessors() {
            var services = new ServiceCollection();

            services.AddAggregates().AddSagas(o => o.ScanTypes(typeof(PlainContractSaga)));

            Registrations(services).Should().ContainSingle().Which.Predecessors.Should().BeEmpty();
        }
        [Fact]
        public void GivenAClassWithTwoSagaInterfaces_Throws_WithAHint() {
            var add = () => new ServiceCollection().AddAggregates().AddSagas(o => o.ScanTypes(typeof(DoubleSaga)));

            add.Should().Throw<InvalidOperationException>()
                .WithMessage($"*{typeof(DoubleSaga).FullName}*ISaga<TSagaState, TEvent> once*marker interface*");
        }

        [Fact]
        public void GivenASagaWithDeadlines_StartsItsSubscription() {
            var services = new ServiceCollection();

            services.AddAggregates().AddSagas(o => o.ScanTypes(typeof(DeadlineTestSaga)));

            services.Should().ContainSingle(d => d.ServiceType == typeof(DeadlineTestSaga));
            services.Should().ContainSingle(d => d.ServiceType == typeof(IHostedService));
            Registrations(services).Should().ContainSingle()
                .Which.EventType.Should().Be(typeof(TestEvent));
        }

        [Fact]
        public void GivenTwoDeadlineInterfaces_Throws_WithAHint() {
            var add = () => new ServiceCollection().AddAggregates().AddSagas(o => o.ScanTypes(typeof(TwoDeadlinesSaga)));

            add.Should().Throw<InvalidOperationException>()
                .WithMessage($"*{typeof(TwoDeadlinesSaga).FullName}*2 times*marker interface*");
        }

        [Fact]
        public void GivenTwoClassesWithTheSameContract_Throws() {
            var add = () => new ServiceCollection().AddAggregates().AddSagas(o => o
                .ScanTypes(typeof(PlainContractSaga), typeof(SameContractSaga)));

            add.Should().Throw<InvalidOperationException>().WithMessage("*'Fulfillment@v1'*");
        }

        [Fact]
        public void GivenAPolicyWithTheSameContract_Throws() {
            var services = new ServiceCollection();
            var builder = services.AddAggregates();
            builder.AddPolicies(o => o.ScanTypes(typeof(FulfillmentPolicy)));

            var add = () => builder.AddSagas(o => o.ScanTypes(typeof(PlainContractSaga)));

            add.Should().Throw<InvalidOperationException>()
                .WithMessage($"*'Fulfillment@v1'*{typeof(FulfillmentPolicy).FullName}*{typeof(PlainContractSaga).FullName}*");
        }

        [Fact]
        public async Task GivenTwoSagasOnTheSameEvent_EachCorrelatesItToItsOwnIds() {
            var token = TestContext.Current.CancellationToken;
            var factory = new FakeSubscriptionFactory(FakeSubscriptionFactory.Messages(1, _ => new TestEvent(1)));
            var firstRepository = EmptyRepository<FirstState>();
            var secondRepository = EmptyRepository<SecondState>();
            var services = new ServiceCollection()
                .AddLogging()
                .AddSingleton<HandlerProbe>()
                .AddSingleton<ISubscriptionFactory>(factory)
                .AddSingleton(A.Fake<ICheckpointStore>())
                .AddSingleton<SagaCommitDelegate>(_ => ValueTask.CompletedTask)
                .AddSingleton(A.Fake<IScheduledStore>())
                .AddSingleton(typeof(SchedulerActivation<>))
                .AddSingleton(firstRepository)
                .AddSingleton(secondRepository);
            services.AddAggregates().AddSagas(o => o.ScanTypes(typeof(FirstSaga), typeof(SecondSaga)));
            await using var provider = services.BuildServiceProvider();
            var probe = provider.GetRequiredService<HandlerProbe>();
            var hostedServices = provider.GetServices<IHostedService>().ToArray();

            foreach (var hostedService in hostedServices)
                await hostedService.StartAsync(token);
            await Eventually.UntilAsync(() => probe.Count<FirstSaga>() >= 1 && probe.Count<SecondSaga>() >= 1,
                "both sagas handle the event");
            foreach (var hostedService in hostedServices)
                await hostedService.StopAsync(token);

            hostedServices.Should().HaveCount(2);
            probe.Count<FirstSaga>().Should().Be(1);
            probe.Count<SecondSaga>().Should().Be(1);
            LoadedSagaIds(firstRepository).Should().Equal(new AggregateIdentifier("first-1"));
            LoadedSagaIds(secondRepository).Should().Equal(new AggregateIdentifier("second-1"));
        }

        [Fact]
        public async Task Correlate_RunsOnASagaFromAFreshScope_SoItCanUseScopedDependencies() {
            var token = TestContext.Current.CancellationToken;
            var factory = new FakeSubscriptionFactory(FakeSubscriptionFactory.Messages(1, _ => new TestEvent(1)));
            var repository = EmptyRepository<TestSagaState>();
            var services = new ServiceCollection()
                .AddLogging()
                .AddScoped<ScopedPrefix>()
                .AddSingleton<ISubscriptionFactory>(factory)
                .AddSingleton(A.Fake<ICheckpointStore>())
                .AddSingleton<SagaCommitDelegate>(_ => ValueTask.CompletedTask)
                .AddSingleton(A.Fake<IScheduledStore>())
                .AddSingleton(typeof(SchedulerActivation<>))
                .AddSingleton(repository);
            services.AddAggregates().AddSagas(o => o.ScanTypes(typeof(ScopedSaga)));
            await using var provider = services.BuildServiceProvider(new ServiceProviderOptions { ValidateScopes = true });
            var hostedServices = provider.GetServices<IHostedService>().ToArray();

            foreach (var hostedService in hostedServices)
                await hostedService.StartAsync(token);
            await Eventually.UntilAsync(() => LoadedSagaIds(repository).Any(), "the saga's repository is loaded");
            foreach (var hostedService in hostedServices)
                await hostedService.StopAsync(token);

            LoadedSagaIds(repository).Should().Equal(new AggregateIdentifier("scoped-1"));
        }

        static ISagaRepository<TState, TestEvent> EmptyRepository<TState>() where TState : IState<TState, TestEvent> {
            var repository = A.Fake<ISagaRepository<TState, TestEvent>>();
            A.CallTo(() => repository.TryGetAsync(A<AggregateIdentifier>._, A<CancellationToken>._))
                .Returns(ValueTask.FromResult<SagaRoot<TState, TestEvent>?>(null));
            return repository;
        }

        static IEnumerable<AggregateIdentifier> LoadedSagaIds<TState>(ISagaRepository<TState, TestEvent> repository) where TState : IState<TState, TestEvent> =>
            Fake.GetCalls(repository)
                .Where(call => call.Method.Name == nameof(ISagaRepository<TState, TestEvent>.TryGetAsync))
                .Select(call => call.GetArgument<AggregateIdentifier>(0));

        [Fact]
        public void RegistersDefaultCheckpointOptionsAndSystemTimeProvider() {
            var services = new ServiceCollection();

            services.AddAggregates().AddSagas();

            using var provider = services.BuildServiceProvider();
            var options = provider.GetRequiredService<SubscriptionCheckpointOptions>();
            options.MaxBatchSize.Should().Be(100);
            options.MaxInterval.Should().Be(TimeSpan.FromSeconds(5));
            provider.GetRequiredService<TimeProvider>().Should().BeSameAs(TimeProvider.System);
        }

        [Fact]
        public void KeepsCheckpointOptionsRegisteredBefore() {
            var services = new ServiceCollection();
            var options = new SubscriptionCheckpointOptions { MaxBatchSize = 10 };
            services.AddSingleton(options);

            services.AddAggregates().AddSagas();

            services.Should().ContainSingle(d => d.ServiceType == typeof(SubscriptionCheckpointOptions))
                .Which.ImplementationInstance.Should().BeSameAs(options);
        }

        [Fact]
        public void RegistersSubscriptionLoopWithDefaultResubscribeOptions() {
            var services = new ServiceCollection();

            services.AddAggregates().AddSagas();

            services.Should().ContainSingle(d => d.ServiceType == typeof(SubscriptionLoop))
                .Which.Lifetime.Should().Be(ServiceLifetime.Singleton);
            using var provider = services.BuildServiceProvider();
            provider.GetRequiredService<SubscriptionResubscribeOptions>().MaxDelay.Should().Be(TimeSpan.FromSeconds(30));
        }

        [Fact]
        public void ErrorMode_IsTheOnErrorOfTheContract() {
            var services = new ServiceCollection();

            services.AddAggregates().AddSagas(o => o
                .ScanTypes(typeof(BlockingSaga), typeof(PlainContractSaga)));

            Registrations(services).Should().ContainSingle(r => r.SubscriptionId == "Outbox@v1")
                .Which.ErrorMode.Should().Be(SubscriptionErrorMode.Block);
            Registrations(services).Should().ContainSingle(r => r.SubscriptionId == "Fulfillment@v1")
                .Which.ErrorMode.Should().Be(SubscriptionErrorMode.Park);
        }

        [Fact]
        public void RegistersEachHandlerAsAReplayableSubscription() {
            var services = new ServiceCollection();

            services.AddAggregates().AddSagas(o => o
                .ScanTypes(typeof(BlockingSaga), typeof(PlainContractSaga)));

            services.Count(d => d.ServiceType == typeof(IReplayableSubscription)).Should().Be(2);
            services.Count(d => d.ServiceType == typeof(IHostedService)).Should().Be(2);
            services.Should().ContainSingle(d => d.ServiceType == typeof(IParkedMessages));
        }

        internal sealed record FirstState : IState<FirstState, TestEvent> {
            public static FirstState Initial => new();
            public FirstState Apply(TestEvent @event) => this;
        }

        internal sealed record SecondState : IState<SecondState, TestEvent> {
            public static SecondState Initial => new();
            public SecondState Apply(TestEvent @event) => this;
        }

        abstract class ProbeSaga<TState>(HandlerProbe probe, string prefix) : ISaga<TState, TestEvent>
            where TState : IState<TState, TestEvent> {
            public IEnumerable<AggregateIdentifier> Correlate(TestEvent @event, EventMetadata metadata) =>
                [new AggregateIdentifier($"{prefix}-{@event.Value}")];

            public async IAsyncEnumerable<ICommand> ReactAsync(TState state, TestEvent @event, [EnumeratorCancellation] CancellationToken cancellationToken = default) {
                probe.Record(this, @event);
                yield break;
            }
        }

        [SagaContract("First")]
        sealed class FirstSaga(HandlerProbe probe) : ProbeSaga<FirstState>(probe, "first");

        [SagaContract("Second")]
        sealed class SecondSaga(HandlerProbe probe) : ProbeSaga<SecondState>(probe, "second");

        class ScopedPrefix {
            public string Value => "scoped";
        }

        [SagaContract("Scoped")]
        sealed class ScopedSaga(ScopedPrefix prefix) : ISaga<TestSagaState, TestEvent> {
            public IEnumerable<AggregateIdentifier> Correlate(TestEvent @event, EventMetadata metadata) =>
                [new AggregateIdentifier($"{prefix.Value}-{@event.Value}")];

            public IAsyncEnumerable<ICommand> ReactAsync(TestSagaState state, TestEvent @event, CancellationToken cancellationToken = default) =>
                AsyncEnumerable.Empty<ICommand>();
        }

        [SagaContract("Fulfillment")]
        sealed class PlainContractSaga : ISaga<TestSagaState, TestEvent> {
            public IEnumerable<AggregateIdentifier> Correlate(TestEvent @event, EventMetadata metadata) => [];

            public IAsyncEnumerable<ICommand> ReactAsync(TestSagaState state, TestEvent @event, CancellationToken cancellationToken = default) =>
                AsyncEnumerable.Empty<ICommand>();
        }

        [SagaContract("Fulfillment")]
        sealed class SameContractSaga : ISaga<TestSagaState, TestEvent> {
            public IEnumerable<AggregateIdentifier> Correlate(TestEvent @event, EventMetadata metadata) => [];

            public IAsyncEnumerable<ICommand> ReactAsync(TestSagaState state, TestEvent @event, CancellationToken cancellationToken = default) =>
                AsyncEnumerable.Empty<ICommand>();
        }

        [SagaContract("Fulfillment", version: 2, @namespace: "Orders", startFromEnd: true)]
        sealed class FullContractSaga : ISaga<TestSagaState, TestEvent> {
            public IEnumerable<AggregateIdentifier> Correlate(TestEvent @event, EventMetadata metadata) => [];

            public IAsyncEnumerable<ICommand> ReactAsync(TestSagaState state, TestEvent @event, CancellationToken cancellationToken = default) =>
                AsyncEnumerable.Empty<ICommand>();
        }

        [SagaContract("Outbox", onError: SubscriptionErrorMode.Block)]
        sealed class BlockingSaga : ISaga<TestSagaState, TestEvent> {
            public IEnumerable<AggregateIdentifier> Correlate(TestEvent @event, EventMetadata metadata) => [];

            public IAsyncEnumerable<ICommand> ReactAsync(TestSagaState state, TestEvent @event, CancellationToken cancellationToken = default) =>
                AsyncEnumerable.Empty<ICommand>();
        }

        [SagaContract("Double")]
        sealed class DoubleSaga : ISaga<TestSagaState, TestEvent>, ISaga<OtherTestSagaState, OtherTestEvent> {
            public IEnumerable<AggregateIdentifier> Correlate(TestEvent @event, EventMetadata metadata) => [];

            public IEnumerable<AggregateIdentifier> Correlate(OtherTestEvent @event, EventMetadata metadata) => [];

            public IAsyncEnumerable<ICommand> ReactAsync(TestSagaState state, TestEvent @event, CancellationToken cancellationToken = default) =>
                AsyncEnumerable.Empty<ICommand>();

            public IAsyncEnumerable<ICommand> ReactAsync(OtherTestSagaState state, OtherTestEvent @event, CancellationToken cancellationToken = default) =>
                AsyncEnumerable.Empty<ICommand>();
        }

        [SagaContract("TwoDeadlines")]
        sealed class TwoDeadlinesSaga : ISaga<TestSagaState, TestEvent, TestDeadline>, ISaga<TestSagaState, TestEvent, string> {
            public IEnumerable<AggregateIdentifier> Correlate(TestEvent @event, EventMetadata metadata) => [];

            public IAsyncEnumerable<ICommand> ReactAsync(TestSagaState state, TestEvent @event, CancellationToken cancellationToken = default) =>
                AsyncEnumerable.Empty<ICommand>();

            public IAsyncEnumerable<ICommand> ReactToDeadlineAsync(TestSagaState state, TestDeadline deadline, CancellationToken cancellationToken = default) =>
                AsyncEnumerable.Empty<ICommand>();

            public IAsyncEnumerable<ICommand> ReactToDeadlineAsync(TestSagaState state, string deadline, CancellationToken cancellationToken = default) =>
                AsyncEnumerable.Empty<ICommand>();
        }

        [PolicyContract("Fulfillment")]
        sealed class FulfillmentPolicy : IPolicy<TestEvent> {
            public IAsyncEnumerable<ICommand> ReactAsync(TestEvent @event, CancellationToken cancellationToken = default) =>
                AsyncEnumerable.Empty<ICommand>();
        }
    }
}
