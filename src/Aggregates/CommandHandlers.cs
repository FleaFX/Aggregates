﻿using System.Reflection;

namespace Aggregates;

/// <summary>
/// Marker interface for command handlers.
/// </summary>
/// <typeparam name="TCommand">The type of the command that affects the state of the aggregate.</typeparam>
/// <typeparam name="TState">The type of the maintained state object.</typeparam>
/// <typeparam name="TEvent">The type of the event(s) that are applicable.</typeparam>
public interface ICommandHandler<in TCommand, TState, TEvent>
    where TCommand : ICommand<TCommand, TState, TEvent>
    where TState : IState<TState, TEvent> {
    /// <summary>
    /// Asynchronously handles the given <paramref name="command"/>.
    /// </summary>
    /// <param name="command">The command object to handle.</param>
    /// <returns>A <see cref="ValueTask"/> that represents the asynchronous operation.</returns>
    ValueTask HandleAsync(TCommand command);
}

/// <summary>
/// Handler that creates a new <see cref="AggregateRoot{TState,TEvent}"/> object and adds it to the repository.
/// </summary>
/// <typeparam name="TCommand">The type of the command that affects the state of the aggregate.</typeparam>
/// <typeparam name="TState">The type of the maintained state object.</typeparam>
/// <typeparam name="TEvent">The type of the event(s) that are applicable.</typeparam>
class CreationHandler<TCommand, TState, TEvent> : ICommandHandler<TCommand, TState, TEvent>
    where TCommand : ICommand<TCommand, TState, TEvent>
    where TState : IState<TState, TEvent> {
    readonly IRepository<TState, TEvent> _repository;

    /// <summary>
    /// Initializes a new <see cref="CreationHandler{TCommand,TState,TEvent}"/>.
    /// </summary>
    /// <param name="repository">The <see cref="IRepository{TState,TEvent}"/> to use when retrieving the aggregate which is affected by the handled command.</param>
    public CreationHandler(IRepository<TState, TEvent> repository) =>
        _repository = repository ?? throw new ArgumentNullException(nameof(repository));

    /// <summary>
    /// Asynchronously handles the given <paramref name="command"/>.
    /// </summary>
    /// <param name="command">The command object to handle.</param>
    /// <returns>A <see cref="ValueTask"/> that represents the asynchronous operation.</returns>
    public async ValueTask HandleAsync(TCommand command) {
        var aggregateRoot = new AggregateRoot<TState, TEvent>(TState.Initial, AggregateVersion.None);
        await aggregateRoot.AcceptAsync(command);
        _repository.Add(command, aggregateRoot);
    }
}

/// <summary>
/// Handler that retrieves an existing <see cref="AggregateRoot{TState,TEvent}"/> object from the repository and uses the given command to affect its state.
/// </summary>
/// <typeparam name="TCommand">The type of the command that affects the state of the aggregate.</typeparam>
/// <typeparam name="TState">The type of the maintained state object.</typeparam>
/// <typeparam name="TEvent">The type of the event(s) that are applicable.</typeparam>
class ModificationHandler<TCommand, TState, TEvent> : ICommandHandler<TCommand, TState, TEvent>
    where TCommand : ICommand<TCommand, TState, TEvent>
    where TState : IState<TState, TEvent> {
    readonly IRepository<TState, TEvent> _repository;

    /// <summary>
    /// Initializes a new <see cref="ModificationHandler{TCommand,TState,TEvent}"/>.
    /// </summary>
    /// <param name="repository">The <see cref="IRepository{TState,TEvent}"/> to use when retrieving the aggregate which is affected by the handled command.</param>
    public ModificationHandler(IRepository<TState, TEvent> repository) =>
        _repository = repository ?? throw new ArgumentNullException(nameof(repository));

    /// <summary>
    /// Asynchronously handles the given <paramref name="command"/>.
    /// </summary>
    /// <param name="command">The command object to handle.</param>
    /// <returns>A <see cref="ValueTask"/> that represents the asynchronous operation.</returns>
    public async ValueTask HandleAsync(TCommand command) {
        var aggregateRoot = await _repository.GetAggregateRootAsync(command);
        await aggregateRoot.AcceptAsync(command);
    }
}

/// <summary>
/// Provides the type to use when checking whether commands are creation commands.
/// </summary>
delegate Type MarkerInterfaceTypeProviderDelegate();

/// <summary>
/// Handler that decides whether a given command creates a new or affects an existing <see cref="AggregateRoot{TState,TEvent}"/> object and acts accordingly.
/// </summary>c
class DefaultHandler<TCommand, TState, TEvent> : ICommandHandler<TCommand, TState, TEvent>
    where TCommand : ICommand<TCommand, TState, TEvent>
    where TState : IState<TState, TEvent> {
    readonly ICommandHandler<TCommand, TState, TEvent> _handler;

    /// <summary>
    /// Initializes a new <see cref="DefaultHandler{TCommand,TState,TEvent}"/>.
    /// </summary>
    /// <param name="creationHandler">The <see cref="ICommandHandler{TCommand,TState,TEvent}"/> to invoke when the handled command is the initial command for an aggregate (marked by <see cref="IInitialCommand"/>.)</param>
    /// <param name="modificationHandler">The <see cref="ICommandHandler{TCommand,TState,TEvent}"/> to invoke when the handled command is not the initial command for an aggregate.</param>
    /// <param name="markerInterfaceTypeProvider">Provides the type of the marker interface to look for on commands.</param>
    public DefaultHandler(
        CreationHandler<TCommand, TState, TEvent> creationHandler,
        ModificationHandler<TCommand, TState, TEvent> modificationHandler,
        MarkerInterfaceTypeProviderDelegate markerInterfaceTypeProvider) {
        _handler = typeof(TCommand).IsAssignableTo(markerInterfaceTypeProvider()) ? creationHandler : modificationHandler;
    }

    /// <summary>
    /// Asynchronously handles the given <paramref name="command"/>.
    /// </summary>
    /// <param name="command">The command object to handle.</param>
    /// <returns>A <see cref="ValueTask"/> that represents the asynchronous operation.</returns>
    public ValueTask HandleAsync(TCommand command) =>
        _handler.HandleAsync(command);
}

/// <summary>
/// Handlers that first tries to retrieve an <see cref="AggregateRoot{TState,TEvent}"/> object, and if it doesn't exist yet, adds it to the repository.
/// </summary>
class GetOrAddHandler<TCommand, TState, TEvent> : ICommandHandler<TCommand, TState, TEvent>
    where TCommand : ICommand<TCommand, TState, TEvent>
    where TState : IState<TState, TEvent> {
    readonly IRepository<TState, TEvent> _repository;

    /// <summary>
    /// Initializes a new <see cref="GetOrAddHandler{TCommand,TState,TEvent}"/>.
    /// </summary>
    /// <param name="repository">The <see cref="IRepository{TState,TEvent}"/> to use when interacting with the aggregate which is affected by the handled command.</param>
    public GetOrAddHandler(IRepository<TState, TEvent> repository) =>
        _repository = repository ?? throw new ArgumentNullException(nameof(repository));

    /// <summary>
    /// Asynchronously handles the given <paramref name="command"/>.
    /// </summary>
    /// <param name="command">The command object to handle.</param>
    /// <returns>A <see cref="ValueTask"/> that represents the asynchronous operation.</returns>
    public async ValueTask HandleAsync(TCommand command) {
        var aggregateRoot = await _repository.TryGetAggregateRootAsync(command);
        if (aggregateRoot is null) {
            aggregateRoot = new AggregateRoot<TState, TEvent>(TState.Initial, AggregateVersion.None);
            _repository.Add(command, aggregateRoot);
        }
        await aggregateRoot.AcceptAsync(command);
    }
}

interface ICommandHandlerFactory {
    ICommandHandler<TCommand, TState, TEvent> Create<TCommand, TState, TEvent>()
        where TCommand : ICommand<TCommand, TState, TEvent>
        where TState : IState<TState, TEvent>;
}

/// <summary>
/// Handler that commits changes tracked by the given <see cref="UnitOfWork"/>.
/// </summary>DefaultHandler
class UnitOfWorkAwareHandler<TCommand, TState, TEvent> : ICommandHandler<TCommand, TState, TEvent>
    where TCommand : ICommand<TCommand, TState, TEvent>
    where TState : IState<TState, TEvent> {
    readonly UnitOfWork _unitOfWork;
    readonly CommitDelegate _commitDelegate;
    readonly ICommandHandler<TCommand, TState, TEvent> _handler;

    /// <summary>
    /// Initializes a new <see cref="UnitOfWorkAwareHandler{TCommand,TState,TEvent}"/>.
    /// </summary>
    /// <param name="unitOfWork">The <see cref="UnitOfWork"/> that tracks changes.</param>
    /// <param name="commitDelegate">The <see cref="CommitDelegate"/> that commits the changes made.</param>
    /// <param name="handlerFactory">Provides the handler that performs the actual work.</param>
    public UnitOfWorkAwareHandler(UnitOfWork unitOfWork, CommitDelegate commitDelegate, ICommandHandlerFactory handlerFactory) {
        _unitOfWork = unitOfWork ?? throw new ArgumentNullException(nameof(unitOfWork));
        _commitDelegate = commitDelegate ?? throw new ArgumentNullException(nameof(commitDelegate));
        _handler = handlerFactory.Create<TCommand, TState, TEvent>();
    }

    /// <summary>
    /// Asynchronously handles the given <paramref name="command"/>.
    /// </summary>
    /// <param name="command">The command object to handle.</param>
    /// <returns>A <see cref="ValueTask"/> that represents the asynchronous operation.</returns>
    public async ValueTask HandleAsync(TCommand command) {
        await using var scope = new UnitOfWorkScope(_unitOfWork, _commitDelegate);
        await _handler.HandleAsync(command);
        scope.Complete();
    }
}

class MetadataAwareHandler<TCommand, TState, TEvent> : ICommandHandler<TCommand, TState, TEvent>
    where TCommand : ICommand<TCommand, TState, TEvent>
    where TState : IState<TState, TEvent> {
    readonly UnitOfWorkAwareHandler<TCommand, TState, TEvent> _handler;

    /// <summary>
    /// Initializes a new <see cref="MetadataAwareHandler{TCommand,TState,TEvent}"/>.
    /// </summary>
    /// <param name="handler">The <see cref="UnitOfWorkAwareHandler{TCommand,TState,TEvent}"/> to delegate to.</param>
    public MetadataAwareHandler(UnitOfWorkAwareHandler<TCommand, TState, TEvent> handler) =>
        _handler = handler;

    /// <summary>
    /// Asynchronously handles the given <paramref name="command"/>.
    /// </summary>
    /// <param name="command">The command object to handle.</param>
    /// <returns>A <see cref="ValueTask"/> that represents the asynchronous operation.</returns>
    public async ValueTask HandleAsync(TCommand command) {
        await using var scope = new MetadataScope();

        // commands may provide a context for metadata, they should be attributed with the MetadataAttributes to achieve this
        foreach (var metadata in command.GetType().GetCustomAttributes<MetadataAttribute>())
            scope.Add(metadata.Create(command));

        await _handler.HandleAsync(command);
    }
}