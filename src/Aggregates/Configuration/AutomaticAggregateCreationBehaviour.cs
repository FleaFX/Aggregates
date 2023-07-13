using Aggregates.Entities.Handlers;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Aggregates.Configuration;

class AutomaticAggregateCreationBehaviour : AggregateCreationBehaviour {
    internal override IServiceCollection Configure(IServiceCollection services) {
        services.TryAddScoped(typeof(GetOrAddHandler<,,>));
        services.TryAddScoped<ICommandHandlerFactory>(sp => new AutomaticCommandHandlerFactory(sp));

        return services;
    }

    class AutomaticCommandHandlerFactory : ICommandHandlerFactory {
        readonly IServiceProvider _serviceProvider;

        public AutomaticCommandHandlerFactory(IServiceProvider serviceProvider) =>
            _serviceProvider = serviceProvider;

        public ICommandHandler<TCommand, TState, TEvent> Create<TCommand, TState, TEvent>()
            where TCommand : ICommand<TState, TEvent>
            where TState : IState<TState, TEvent> =>
            _serviceProvider.GetRequiredService<GetOrAddHandler<TCommand, TState, TEvent>>();
    }
}