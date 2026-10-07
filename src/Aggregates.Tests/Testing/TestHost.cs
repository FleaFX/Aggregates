using Aggregates.Policies;
using Aggregates.Projections;
using Aggregates.Sagas;
using Aggregates.Subscriptions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace Aggregates.Testing;

/// <summary>
/// Configures which scenario types a <see cref="TestHost"/> registers. Only the types given
/// here are registered, so that other test types in the assembly stay out of the host.
/// </summary>
sealed class TestHostOptions {
    internal List<Type> EventTypes { get; } = [];
    internal List<Type> CommandTypes { get; } = [];
    internal List<Type> ProjectionTypes { get; } = [];
    internal List<Type> PolicyTypes { get; } = [];
    internal bool UseProjections { get; private set; }
    internal bool UsePolicies { get; private set; }
    internal Action<SagasOptions>? ConfigureSagas { get; private set; }
    internal Action<IServiceCollection>? ConfigureServices { get; private set; }

    /// <summary>
    /// The event types the serializer knows; other stored event types deserialize to <see langword="null"/>.
    /// </summary>
    public TestHostOptions Events(params Type[] types) {
        EventTypes.AddRange(types);
        return this;
    }

    /// <summary>
    /// The commands to register handlers for.
    /// </summary>
    public TestHostOptions Commands(params Type[] types) {
        CommandTypes.AddRange(types);
        return this;
    }

    /// <summary>
    /// The projections to register, each with its subscription. Without types, only the
    /// transport's subscription infrastructure is registered.
    /// </summary>
    public TestHostOptions Projections(params Type[] types) {
        UseProjections = true;
        ProjectionTypes.AddRange(types);
        return this;
    }

    /// <summary>
    /// The policies to register, each with its subscription. Without types, only the
    /// transport's subscription infrastructure is registered.
    /// </summary>
    public TestHostOptions Policies(params Type[] types) {
        UsePolicies = true;
        PolicyTypes.AddRange(types);
        return this;
    }

    /// <summary>
    /// Registers sagas; use <see cref="SagasOptions.ScanTypes"/> and <see cref="SagasOptions.WithResolver{TEvent}(ISagaIdResolver{TEvent})"/>.
    /// </summary>
    public TestHostOptions Sagas(Action<SagasOptions> configure) {
        ConfigureSagas += configure;
        return this;
    }

    /// <summary>
    /// Additional service registrations, applied after Aggregates has been configured.
    /// </summary>
    public TestHostOptions Services(Action<IServiceCollection> configure) {
        ConfigureServices += configure;
        return this;
    }
}

/// <summary>
/// A generic host with Aggregates wired against an <see cref="IStoreFixture"/>: logging to the
/// test output, a short shutdown timeout, fast subscription retries, and a <see cref="HandlerProbe"/>.
/// </summary>
/// <remarks>
/// <see cref="BackgroundServiceExceptionBehavior"/> stays at its default (<c>StopHost</c>), so a
/// test can observe that a failing subscription stops the host through <see cref="Stopping"/>.
/// </remarks>
sealed class TestHost : IAsyncDisposable {
    readonly IHost _host;
    readonly TaskCompletionSource _stopping = new(TaskCreationOptions.RunContinuationsAsynchronously);

    TestHost(IHost host, HandlerProbe probe, SerializationSetup serialization) {
        _host = host;
        Probe = probe;
        Serialization = serialization;
        host.Services.GetRequiredService<IHostApplicationLifetime>().ApplicationStopping.Register(() => _stopping.TrySetResult());
    }

    /// <summary>
    /// What the scenario's handlers and the serializer did in this host.
    /// </summary>
    public HandlerProbe Probe { get; }

    /// <summary>
    /// The serialization the host uses, for writing or reading events outside of Aggregates.
    /// </summary>
    public SerializationSetup Serialization { get; }

    /// <summary>
    /// The host's root service provider.
    /// </summary>
    public IServiceProvider Services => _host.Services;

    /// <summary>
    /// Completes when the host starts stopping, whether by <see cref="DisposeAsync"/> or because a
    /// background service failed.
    /// </summary>
    public Task Stopping => _stopping.Task;

    /// <summary>
    /// Builds and starts a host on <paramref name="store"/> with the scenario types from
    /// <paramref name="configure"/>.
    /// </summary>
    public static async Task<TestHost> StartAsync(IStoreFixture store, ITestOutputHelper output, Action<TestHostOptions> configure) {
        var options = new TestHostOptions();
        configure(options);

        var probe = new HandlerProbe();
        var serialization = new SerializationSetup(probe, options.EventTypes);

        var builder = Host.CreateApplicationBuilder();
        builder.Logging.ClearProviders();
        builder.Logging.AddProvider(new TestOutputLoggerProvider(output));
        builder.Logging.SetMinimumLevel(LogLevel.Debug);
        builder.Services.Configure<HostOptions>(o => o.ShutdownTimeout = TimeSpan.FromSeconds(5));
        builder.Services.AddSingleton(probe);

        // Registered before the Add* calls below, which only add their defaults when absent.
        builder.Services.AddSingleton(new SubscriptionErrorHandlingOptions {
            InitialDelay = TimeSpan.FromMilliseconds(10),
            MaxDelay = TimeSpan.FromMilliseconds(50),
            MaxRetries = 2
        });

        var aggregates = builder.Services.AddAggregates(o => o.ScanTypes([.. options.CommandTypes]));
        store.ConfigureAggregates(aggregates, serialization);

        if (options.UseProjections)
            store.ConfigureProjections(builder.Services.AddProjections(o => o.ScanTypes([.. options.ProjectionTypes])));
        if (options.UsePolicies)
            store.ConfigurePolicies(aggregates.AddPolicies(o => o.ScanTypes([.. options.PolicyTypes])));
        if (options.ConfigureSagas is { } configureSagas)
            store.ConfigureSagas(aggregates.AddSagas(configureSagas));

        options.ConfigureServices?.Invoke(builder.Services);

        var host = builder.Build();
        await host.StartAsync(TestContext.Current.CancellationToken);
        return new TestHost(host, probe, serialization);
    }

    /// <summary>
    /// Handles <paramref name="command"/> in its own scope, as an application would.
    /// </summary>
    public async Task SendAsync<TCommand>(TCommand command) where TCommand : ICommand {
        await using var scope = _host.Services.CreateAsyncScope();
        await scope.ServiceProvider.GetRequiredService<ICommandHandler<TCommand>>().HandleAsync(command, TestContext.Current.CancellationToken);
    }

    /// <summary>
    /// Stops the host and disposes it. The store is left untouched.
    /// </summary>
    public async ValueTask DisposeAsync() {
        try {
            await _host.StopAsync(CancellationToken.None);
        } finally {
            _host.Dispose();
        }
    }
}
