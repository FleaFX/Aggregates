using Aggregates.Entities.Handlers;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Aggregates.Configuration;

class MarkerInterfaceCreationBehaviour<TInterface> : AggregateCreationBehaviour {
    internal override IServiceCollection Configure(IServiceCollection services) {
        services.TryAddScoped(typeof(CreationHandler<,,>));
        services.TryAddScoped(typeof(ModificationHandler<,,>));
        services.TryAddScoped(typeof(DefaultHandler<,,>));
        services.TryAddTransient<MarkerInterfaceTypeProviderDelegate>(_ => static () => typeof(TInterface));
        services.TryAddScoped<ICommandHandlerFactory>(sp => new MarkerInterfaceCommandHandlerFactory(sp));
        return services;
    }

    class MarkerInterfaceCommandHandlerFactory : ICommandHandlerFactory {
        readonly IServiceProvider _serviceProvider;

        public MarkerInterfaceCommandHandlerFactory(IServiceProvider serviceProvider) =>
            _serviceProvider = serviceProvider;

        public ICommandHandler<TCommand, TState, TEvent> Create<TCommand, TState, TEvent>() where TCommand : ICommand<TState, TEvent> where TState : IState<TState, TEvent> =>
            _serviceProvider.GetRequiredService<DefaultHandler<TCommand, TState, TEvent>>();
    }
}