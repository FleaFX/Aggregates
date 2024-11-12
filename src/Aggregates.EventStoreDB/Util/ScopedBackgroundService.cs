using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace Aggregates.EventStoreDB.Util;

/// <summary>
/// A <see cref="IHostedService"/> should normally be registered as a singleton, requiring its injected dependencies to be singletons as well. This class allows you to use dependencies that are registered as scoped.
/// </summary>
abstract class ScopedBackgroundService<TDep1, TDep2, TDep3, TDep4, TDep5> : BackgroundService {
    protected IServiceScopeFactory ServiceScopeFactory { get; }

    /// <summary>
    /// Initializes a new <see cref="ScopedBackgroundService{TDep1,TDep2,TDep3,TDep4,TDep5}"/>.
    /// </summary>
    /// <param name="serviceScopeFactory">A <see cref="IServiceScopeFactory"/> that creates a scope in order to resolve the dependencies.</param>
    protected ScopedBackgroundService(IServiceScopeFactory serviceScopeFactory) =>
        ServiceScopeFactory = serviceScopeFactory ?? throw new ArgumentNullException(nameof(serviceScopeFactory));

    /// <summary>
    /// This method is called when the <see cref="T:Microsoft.Extensions.Hosting.IHostedService" /> starts. The implementation should return a task that represents
    /// the lifetime of the long running operation(s) being performed.
    /// </summary>
    /// <param name="stoppingToken">Triggered when <see cref="M:Microsoft.Extensions.Hosting.IHostedService.StopAsync(System.Threading.CancellationToken)" /> is called.</param>
    /// <returns>A <see cref="T:System.Threading.Tasks.Task" /> that represents the long running operations.</returns>
    /// <remarks>See <see href="https://docs.microsoft.com/dotnet/core/extensions/workers">Worker Services in .NET</see> for implementation guidelines.</remarks>
    protected sealed override async Task ExecuteAsync(CancellationToken stoppingToken) {
        using var scope = ServiceScopeFactory.CreateScope();
        await ExecuteCoreAsync(
            scope.ServiceProvider.GetRequiredService<TDep1>(),
            scope.ServiceProvider.GetRequiredService<TDep2>(),
            scope.ServiceProvider.GetRequiredService<TDep3>(),
            scope.ServiceProvider.GetRequiredService<TDep4>(),
            scope.ServiceProvider.GetRequiredService<TDep5>(),
            stoppingToken);
    }

    /// <summary>
    /// Implement this method rather than <see cref="ExecuteAsync"/>. The required dependencies are passed in.
    /// </summary>
    /// <param name="dep1">The first scoped dependency.</param>
    /// <param name="dep2">The second scoped dependency.</param>
    /// <param name="dep3">The third scoped dependency.</param>
    /// <param name="dep4">The fourth scoped dependency.</param>
    /// <param name="dep5">The fifth scoped dependency.</param>
    /// <param name="stoppingToken">A <see cref="CancellationToken"/> that is signaled when the asynchronous operation should be stopped.</param>
    /// <returns>A <see cref="Task"/> that represents the asynchronous operation.</returns>
    protected abstract Task ExecuteCoreAsync(TDep1 dep1, TDep2 dep2, TDep3 dep3, TDep4 dep4, TDep5 dep5, CancellationToken stoppingToken);
}

/// <summary>
/// A <see cref="IHostedService"/> should normally be registered as a singleton, requiring its injected dependencies to be singletons as well. This class allows you to use dependencies that are registered as scoped.
/// </summary>
abstract class ScopedBackgroundService<TDep1, TDep2, TDep3, TDep4, TDep5, TDep6> : BackgroundService {
    protected IServiceScopeFactory ServiceScopeFactory { get; }

    /// <summary>
    /// Initializes a new <see cref="ScopedBackgroundService{TDep1,TDep2,TDep3,TDep4,TDep5,TDep6}"/>.
    /// </summary>
    /// <param name="serviceScopeFactory">A <see cref="IServiceScopeFactory"/> that creates a scope in order to resolve the dependencies.</param>
    protected ScopedBackgroundService(IServiceScopeFactory serviceScopeFactory) =>
        ServiceScopeFactory = serviceScopeFactory ?? throw new ArgumentNullException(nameof(serviceScopeFactory));

    /// <summary>
    /// This method is called when the <see cref="T:Microsoft.Extensions.Hosting.IHostedService" /> starts. The implementation should return a task that represents
    /// the lifetime of the long running operation(s) being performed.
    /// </summary>
    /// <param name="stoppingToken">Triggered when <see cref="M:Microsoft.Extensions.Hosting.IHostedService.StopAsync(System.Threading.CancellationToken)" /> is called.</param>
    /// <returns>A <see cref="T:System.Threading.Tasks.Task" /> that represents the long running operations.</returns>
    /// <remarks>See <see href="https://docs.microsoft.com/dotnet/core/extensions/workers">Worker Services in .NET</see> for implementation guidelines.</remarks>
    protected sealed override async Task ExecuteAsync(CancellationToken stoppingToken) {
        using var scope = ServiceScopeFactory.CreateScope();
        await ExecuteCoreAsync(
            scope.ServiceProvider.GetRequiredService<TDep1>(),
            scope.ServiceProvider.GetRequiredService<TDep2>(),
            scope.ServiceProvider.GetRequiredService<TDep3>(),
            scope.ServiceProvider.GetRequiredService<TDep4>(),
            scope.ServiceProvider.GetRequiredService<TDep5>(),
            scope.ServiceProvider.GetRequiredService<TDep6>(),
            stoppingToken);
    }

    /// <summary>
    /// Implement this method rather than <see cref="ExecuteAsync"/>. The required dependencies are passed in.
    /// </summary>
    /// <param name="dep1">The first scoped dependency.</param>
    /// <param name="dep2">The second scoped dependency.</param>
    /// <param name="dep3">The third scoped dependency.</param>
    /// <param name="dep4">The fourth scoped dependency.</param>
    /// <param name="dep5">The fifth scoped dependency.</param>
    /// <param name="dep6">The sixth scoped dependency.</param>
    /// <param name="stoppingToken">A <see cref="CancellationToken"/> that is signaled when the asynchronous operation should be stopped.</param>
    /// <returns>A <see cref="Task"/> that represents the asynchronous operation.</returns>
    protected abstract Task ExecuteCoreAsync(TDep1 dep1, TDep2 dep2, TDep3 dep3, TDep4 dep4, TDep5 dep5, TDep6 dep6, CancellationToken stoppingToken);
}

/// <summary>
/// A <see cref="IHostedService"/> should normally be registered as a singleton, requiring its injected dependencies to be singletons as well. This class allows you to use dependencies that are registered as scoped.
/// </summary>
abstract class ScopedBackgroundService<TDep1, TDep2, TDep3, TDep4, TDep5, TDep6, TDep7> : BackgroundService {
    protected IServiceScopeFactory ServiceScopeFactory { get; }

    /// <summary>
    /// Initializes a new <see cref="ScopedBackgroundService{TDep1,TDep2,TDep3,TDep4,TDep5,TDep6,TDep7}"/>.
    /// </summary>
    /// <param name="serviceScopeFactory">A <see cref="IServiceScopeFactory"/> that creates a scope in order to resolve the dependencies.</param>
    protected ScopedBackgroundService(IServiceScopeFactory serviceScopeFactory) =>
        ServiceScopeFactory = serviceScopeFactory ?? throw new ArgumentNullException(nameof(serviceScopeFactory));

    /// <summary>
    /// This method is called when the <see cref="T:Microsoft.Extensions.Hosting.IHostedService" /> starts. The implementation should return a task that represents
    /// the lifetime of the long running operation(s) being performed.
    /// </summary>
    /// <param name="stoppingToken">Triggered when <see cref="M:Microsoft.Extensions.Hosting.IHostedService.StopAsync(System.Threading.CancellationToken)" /> is called.</param>
    /// <returns>A <see cref="T:System.Threading.Tasks.Task" /> that represents the long running operations.</returns>
    /// <remarks>See <see href="https://docs.microsoft.com/dotnet/core/extensions/workers">Worker Services in .NET</see> for implementation guidelines.</remarks>
    protected sealed override async Task ExecuteAsync(CancellationToken stoppingToken) {
        using var scope = ServiceScopeFactory.CreateScope();
        await ExecuteCoreAsync(
            scope.ServiceProvider.GetRequiredService<TDep1>(),
            scope.ServiceProvider.GetRequiredService<TDep2>(),
            scope.ServiceProvider.GetRequiredService<TDep3>(),
            scope.ServiceProvider.GetRequiredService<TDep4>(),
            scope.ServiceProvider.GetRequiredService<TDep5>(),
            scope.ServiceProvider.GetRequiredService<TDep6>(),
            scope.ServiceProvider.GetRequiredService<TDep7>(),
            stoppingToken);
    }

    /// <summary>
    /// Implement this method rather than <see cref="ExecuteAsync"/>. The required dependencies are passed in.
    /// </summary>
    /// <param name="dep1">The first scoped dependency.</param>
    /// <param name="dep2">The second scoped dependency.</param>
    /// <param name="dep3">The third scoped dependency.</param>
    /// <param name="dep4">The fourth scoped dependency.</param>
    /// <param name="dep5">The fifth scoped dependency.</param>
    /// <param name="dep6">The sixth scoped dependency.</param>
    /// <param name="dep6">The seventh scoped dependency.</param>
    /// <param name="stoppingToken">A <see cref="CancellationToken"/> that is signaled when the asynchronous operation should be stopped.</param>
    /// <returns>A <see cref="Task"/> that represents the asynchronous operation.</returns>
    protected abstract Task ExecuteCoreAsync(TDep1 dep1, TDep2 dep2, TDep3 dep3, TDep4 dep4, TDep5 dep5, TDep6 dep6, TDep7 dep7, CancellationToken stoppingToken);
}

/// <summary>
/// A <see cref="IHostedService"/> should normally be registered as a singleton, requiring its injected dependencies to be singletons as well. This class allows you to use dependencies that are registered as scoped.
/// </summary>
abstract class ScopedBackgroundService<TDep1, TDep2, TDep3, TDep4, TDep5, TDep6, TDep7, TDep8> : BackgroundService {
    protected IServiceScopeFactory ServiceScopeFactory { get; }

    /// <summary>
    /// Initializes a new <see cref="ScopedBackgroundService{TDep1,TDep2,TDep3,TDep4,TDep5,TDep6,TDep7,TDep8}"/>.
    /// </summary>
    /// <param name="serviceScopeFactory">A <see cref="IServiceScopeFactory"/> that creates a scope in order to resolve the dependencies.</param>
    protected ScopedBackgroundService(IServiceScopeFactory serviceScopeFactory) =>
        ServiceScopeFactory = serviceScopeFactory ?? throw new ArgumentNullException(nameof(serviceScopeFactory));

    /// <summary>
    /// This method is called when the <see cref="T:Microsoft.Extensions.Hosting.IHostedService" /> starts. The implementation should return a task that represents
    /// the lifetime of the long running operation(s) being performed.
    /// </summary>
    /// <param name="stoppingToken">Triggered when <see cref="M:Microsoft.Extensions.Hosting.IHostedService.StopAsync(System.Threading.CancellationToken)" /> is called.</param>
    /// <returns>A <see cref="T:System.Threading.Tasks.Task" /> that represents the long running operations.</returns>
    /// <remarks>See <see href="https://docs.microsoft.com/dotnet/core/extensions/workers">Worker Services in .NET</see> for implementation guidelines.</remarks>
    protected sealed override async Task ExecuteAsync(CancellationToken stoppingToken) {
        using var scope = ServiceScopeFactory.CreateScope();
        await ExecuteCoreAsync(
            scope.ServiceProvider.GetRequiredService<TDep1>(),
            scope.ServiceProvider.GetRequiredService<TDep2>(),
            scope.ServiceProvider.GetRequiredService<TDep3>(),
            scope.ServiceProvider.GetRequiredService<TDep4>(),
            scope.ServiceProvider.GetRequiredService<TDep5>(),
            scope.ServiceProvider.GetRequiredService<TDep6>(),
            scope.ServiceProvider.GetRequiredService<TDep7>(),
            scope.ServiceProvider.GetRequiredService<TDep8>(),
            stoppingToken);
    }

    /// <summary>
    /// Implement this method rather than <see cref="ExecuteAsync"/>. The required dependencies are passed in.
    /// </summary>
    /// <param name="dep1">The first scoped dependency.</param>
    /// <param name="dep2">The second scoped dependency.</param>
    /// <param name="dep3">The third scoped dependency.</param>
    /// <param name="dep4">The fourth scoped dependency.</param>
    /// <param name="dep5">The fifth scoped dependency.</param>
    /// <param name="dep6">The sixth scoped dependency.</param>
    /// <param name="dep7">The seventh scoped dependency.</param>
    /// <param name="dep8">The eigth scoped dependency.</param>
    /// <param name="stoppingToken">A <see cref="CancellationToken"/> that is signaled when the asynchronous operation should be stopped.</param>
    /// <returns>A <see cref="Task"/> that represents the asynchronous operation.</returns>
    protected abstract Task ExecuteCoreAsync(TDep1 dep1, TDep2 dep2, TDep3 dep3, TDep4 dep4, TDep5 dep5, TDep6 dep6, TDep7 dep7, TDep8 dep8, CancellationToken stoppingToken);
}

/// <summary>
/// A <see cref="IHostedService"/> should normally be registered as a singleton, requiring its injected dependencies to be singletons as well. This class allows you to use dependencies that are registered as scoped.
/// </summary>
abstract class ScopedBackgroundService<TDep1, TDep2, TDep3, TDep4, TDep5, TDep6, TDep7, TDep8, TDep9> : BackgroundService {
    protected IServiceScopeFactory ServiceScopeFactory { get; }

    /// <summary>
    /// Initializes a new <see cref="ScopedBackgroundService{TDep1,TDep2,TDep3,TDep4,TDep5,TDep6,TDep7,TDep8,TDep9}"/>.
    /// </summary>
    /// <param name="serviceScopeFactory">A <see cref="IServiceScopeFactory"/> that creates a scope in order to resolve the dependencies.</param>
    protected ScopedBackgroundService(IServiceScopeFactory serviceScopeFactory) =>
        ServiceScopeFactory = serviceScopeFactory ?? throw new ArgumentNullException(nameof(serviceScopeFactory));

    /// <summary>
    /// This method is called when the <see cref="T:Microsoft.Extensions.Hosting.IHostedService" /> starts. The implementation should return a task that represents
    /// the lifetime of the long running operation(s) being performed.
    /// </summary>
    /// <param name="stoppingToken">Triggered when <see cref="M:Microsoft.Extensions.Hosting.IHostedService.StopAsync(System.Threading.CancellationToken)" /> is called.</param>
    /// <returns>A <see cref="T:System.Threading.Tasks.Task" /> that represents the long running operations.</returns>
    /// <remarks>See <see href="https://docs.microsoft.com/dotnet/core/extensions/workers">Worker Services in .NET</see> for implementation guidelines.</remarks>
    protected sealed override async Task ExecuteAsync(CancellationToken stoppingToken) {
        using var scope = ServiceScopeFactory.CreateScope();
        await ExecuteCoreAsync(
            scope.ServiceProvider.GetRequiredService<TDep1>(),
            scope.ServiceProvider.GetRequiredService<TDep2>(),
            scope.ServiceProvider.GetRequiredService<TDep3>(),
            scope.ServiceProvider.GetRequiredService<TDep4>(),
            scope.ServiceProvider.GetRequiredService<TDep5>(),
            scope.ServiceProvider.GetRequiredService<TDep6>(),
            scope.ServiceProvider.GetRequiredService<TDep7>(),
            scope.ServiceProvider.GetRequiredService<TDep8>(),
            scope.ServiceProvider.GetRequiredService<TDep9>(),
            stoppingToken);
    }

    /// <summary>
    /// Implement this method rather than <see cref="ExecuteAsync"/>. The required dependencies are passed in.
    /// </summary>
    /// <param name="dep1">The first scoped dependency.</param>
    /// <param name="dep2">The second scoped dependency.</param>
    /// <param name="dep3">The third scoped dependency.</param>
    /// <param name="dep4">The fourth scoped dependency.</param>
    /// <param name="dep5">The fifth scoped dependency.</param>
    /// <param name="dep6">The sixth scoped dependency.</param>
    /// <param name="dep7">The seventh scoped dependency.</param>
    /// <param name="dep8">The eighth scoped dependency.</param>
    /// <param name="dep9">The ninth scoped dependency.</param>
    /// <param name="stoppingToken">A <see cref="CancellationToken"/> that is signaled when the asynchronous operation should be stopped.</param>
    /// <returns>A <see cref="Task"/> that represents the asynchronous operation.</returns>
    protected abstract Task ExecuteCoreAsync(TDep1 dep1, TDep2 dep2, TDep3 dep3, TDep4 dep4, TDep5 dep5, TDep6 dep6, TDep7 dep7, TDep8 dep8, TDep9 dep9, CancellationToken stoppingToken);
}