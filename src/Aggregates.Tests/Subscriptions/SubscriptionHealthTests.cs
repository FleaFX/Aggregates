using Aggregates.Testing;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Microsoft.Extensions.Hosting;

namespace Aggregates.Subscriptions;

/// <summary>
/// Integration tests for the subscription health check: the state that the subscriptions and
/// schedulers of a host report, and the health it adds up to.
/// </summary>
[Trait("Category", "Integration")]
public class SubscriptionHealthTests(ITestOutputHelper output) {
    static readonly TimeSpan PatientTimeout = TimeSpan.FromSeconds(60);

    static void AddHealthCheck(IServiceCollection services) =>
        services.AddHealthChecks().AddCheck<SubscriptionHealthCheck>("aggregates");

    static string IdOf(TestHost host, Type handler) =>
        host.Services.GetServices<SubscriptionRegistration>().Single(registration => registration.HandlerType == handler).SubscriptionId;

    static SubscriptionStatus Status(TestHost host, string id) =>
        host.Services.GetRequiredService<SubscriptionMonitor>().GetStatuses().Single(status => status.Id == id);

    // Null until the subscription or scheduler reports: hosted services start in the background.
    static SubscriptionState? StateOf(TestHost host, string id) =>
        host.Services.GetRequiredService<SubscriptionMonitor>().GetStatuses().SingleOrDefault(status => status.Id == id)?.State;

    static async Task<HealthReportEntry> HealthAsync(TestHost host) =>
        (await host.Services.GetRequiredService<HealthCheckService>().CheckHealthAsync(TestContext.Current.CancellationToken)).Entries["aggregates"];

    [Theory, MemberData(nameof(ScenarioHandlers.KindsAndTransports), MemberType = typeof(ScenarioHandlers))]
    public async Task RunningHost_IsHealthy(HandlerKind kind, Transport transport) {
        await using var store = await Stores.StartAsync(transport);
        var handler = ScenarioHandlers.Probe(kind);
        await using var host = await TestHost.StartAsync(store, output,
            o => o.Events(Orders.EventTypes).Handlers(kind, handler).Services(AddHealthCheck));
        var id = IdOf(host, handler);

        await store.AppendAsync(host.Serialization, "order-1", new OrderPlaced("order-1", "alice"));
        await Eventually.UntilAsync(() => host.Probe.Count(handler) >= 1, "order-1 is handled");

        Status(host, id).State.Should().Be(SubscriptionState.Running);
        if (kind is HandlerKind.Policy or HandlerKind.Saga)
            Status(host, $"scheduled-{id}").State.Should().Be(SubscriptionState.Waiting);

        (await HealthAsync(host)).Status.Should().Be(HealthStatus.Healthy);
    }

    [Theory, MemberData(nameof(ScenarioHandlers.KindsAndTransports), MemberType = typeof(ScenarioHandlers))]
    public async Task BlockingHandler_IsBlocked_AndRunningOnceItSucceeds(HandlerKind kind, Transport transport) {
        await using var store = await Stores.StartAsync(transport);
        var handler = ScenarioHandlers.Blocking(kind);
        await using var host = await TestHost.StartAsync(store, output,
            o => o.Events(Orders.EventTypes).Handlers(kind, handler).Services(AddHealthCheck));
        var id = IdOf(host, handler);

        await store.AppendAsync(host.Serialization, "poison-1", new OrderPlaced("poison-1", "alice"));
        await store.AppendAsync(host.Serialization, "order-2", new OrderPlaced("order-2", "bob"));
        await Eventually.UntilAsync(() => StateOf(host, id) == SubscriptionState.Blocked, "the poisoned event blocks");

        Status(host, id).Position.Should().NotBeNull();
        var health = await HealthAsync(host);
        health.Status.Should().Be(HealthStatus.Degraded);
        health.Description.Should().Contain($"'{id}' is blocked");

        host.Probe.PoisonFixed = true;
        await Eventually.UntilAsync(() => host.Probe.Count(handler) == 2, "the blocking subscription handles both events",
            timeout: PatientTimeout);

        // Reported after the handler returns, so just after the probe counts the event.
        await Eventually.UntilAsync(() => StateOf(host, id) == SubscriptionState.Running, "the subscription no longer blocks");
        (await HealthAsync(host)).Status.Should().Be(HealthStatus.Healthy);
    }

    [Fact]
    public async Task StoreInterruption_IsRetrying_AndRunningAfterTheNextEvent() {
        await using var store = await Stores.StartAsync(Transport.KurrentDB);
        var handler = ScenarioHandlers.Probe(HandlerKind.Projection);
        await using var host = await TestHost.StartAsync(store, output,
            o => o.Events(Orders.EventTypes).Handlers(HandlerKind.Projection, handler).Services(AddHealthCheck));
        var id = IdOf(host, handler);

        await store.AppendAsync(host.Serialization, "order-1", new OrderPlaced("order-1", "alice"));
        await Eventually.UntilAsync(() => host.Probe.Count(handler) >= 1, "order-1 is handled");

        await store.InterruptAsync();
        await Eventually.UntilAsync(() => StateOf(host, id) == SubscriptionState.Retrying,
            "the subscription notices the interruption", TimeSpan.FromSeconds(90));

        (await HealthAsync(host)).Status.Should().Be(HealthStatus.Degraded);

        await store.ResumeAsync();
        await store.AppendAsync(host.Serialization, "order-2", new OrderPlaced("order-2", "bob"));
        await Eventually.UntilAsync(() => host.Probe.Count(handler) >= 2, "order-2 is handled after the interruption",
            TimeSpan.FromSeconds(60));

        // Reported after the handler returns, so just after the probe counts the event.
        await Eventually.UntilAsync(() => StateOf(host, id) == SubscriptionState.Running, "the subscription receives messages again");
        (await HealthAsync(host)).Status.Should().Be(HealthStatus.Healthy);
    }

    [Theory, MemberData(nameof(ScenarioHandlers.KindsAndTransports), MemberType = typeof(ScenarioHandlers))]
    public async Task MissingCheckpoint_UnderIgnore_IsStopped_WhileTheHostKeepsRunning(HandlerKind kind, Transport transport) {
        await using var store = await Stores.StartAsync(transport);
        var handler = ScenarioHandlers.Probe(kind);
        await using var host = await TestHost.StartAsync(store, output, o => o.Events(Orders.EventTypes).Handlers(kind, handler)
            .Services(s => {
                AddHealthCheck(s);
                s.AddSingleton(new SubscriptionCheckpointOptions { RequireCheckpoint = true });
                s.Configure<HostOptions>(o => o.BackgroundServiceExceptionBehavior = BackgroundServiceExceptionBehavior.Ignore);
            }));
        var id = IdOf(host, handler);

        await Eventually.UntilAsync(() => StateOf(host, id) == SubscriptionState.Stopped, "the subscription refuses to start");

        Status(host, id).Error.Should().BeAssignableTo<InvalidOperationException>().Which.Message.Should().Contain("RequireCheckpoint");
        if (kind is HandlerKind.Policy or HandlerKind.Saga) {
            // Its partner's failure cancels the scheduler, which reports a moment later.
            await Eventually.UntilAsync(() => StateOf(host, $"scheduled-{id}") == SubscriptionState.Stopped, "the scheduler stops with it");
            var scheduler = Status(host, $"scheduled-{id}");
            scheduler.State.Should().Be(SubscriptionState.Stopped);
            scheduler.Error.Should().BeNull("the scheduler is stopped by its partner");
        }

        (await HealthAsync(host)).Status.Should().Be(HealthStatus.Unhealthy);
        host.Stopping.IsCompleted.Should().BeFalse("the host keeps running under Ignore");
    }
}
