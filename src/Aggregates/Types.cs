namespace Aggregates;

/// <summary>
/// Uniquely identifies an aggregate within the system.
/// </summary>
/// <param name="Value">The string representation of the identifier.</param>
public record struct AggregateIdentifier(string Value) {
    /// <summary>
    /// Casts the given <see cref="string"/> to a <see cref="AggregateIdentifier"/>.
    /// </summary>
    /// <param name="value">The <see cref="string"/> to cast.</param>
    public static implicit operator AggregateIdentifier(string value) => new(value);
}

/// <summary>
/// Provides an origin and a function to apply an event to the current state.
/// </summary>
/// <typeparam name="TState">The type of the maintained state object.</typeparam>
/// <typeparam name="TEvent">The type of the event(s) that are applicable.</typeparam>
public interface IState<out TState, in TEvent> where TState : IState<TState, TEvent> {
    /// <summary>
    /// Gets the initial state.
    /// </summary>
    static abstract TState Initial { get; }

    /// <summary>
    /// Applies the given <paramref name="event"/> to progress to a new state.
    /// </summary>
    /// <param name="event">The event to apply.</param>
    /// <returns>The new state.</returns>
    TState Apply(TEvent @event);
}

/// <summary>
/// Marker interface for projections, which maintain a state using events sourced from multiple streams.
/// </summary>
public interface IProjection<TState, in TEvent> {
    /// <summary>
    /// Applies the given <paramref name="event"/> to progress to a new state.
    /// </summary>
    /// <param name="event">The event to apply.</param>
    /// <param name="metadata">A set of metadata that was saved with the event, if any.</param>
    /// <returns>The new state.</returns>
    ICommit<TState> Apply(TEvent @event, IReadOnlyDictionary<string, object?>? metadata = null);
}

/// <summary>
/// Reacts to an event by producing new commands to handle.
/// </summary>
/// <typeparam name="TReactionEvent">The type of the event to react to.</typeparam>
/// <typeparam name="TCommand">The type of the produced commands.</typeparam>
/// <typeparam name="TState">The type of the state object that is acted upon by the produced commands.</typeparam>
/// <typeparam name="TEvent">The type of the event(s) that are applicable to the state that is acted upon by the produced commands.</typeparam>
public interface IReaction<in TReactionEvent, out TCommand, TState, TEvent>
    where TState : IState<TState, TEvent>
    where TCommand : ICommand<TCommand, TState, TEvent> {
    /// <summary>
    /// Asynchronously reacts to an event by producing a sequence of commands to handle.
    /// </summary>
    /// <param name="event">The instigating event.</param>
    /// <param name="metadata">A set of metadata that was saved with the event, if any.</param>
    /// <returns>A sequence of commands.</returns>
    IEnumerable<TCommand> React(TReactionEvent @event, IReadOnlyDictionary<string, object?>? metadata = null) =>
        ReactAsync(@event).ToEnumerable();

    /// <summary>
    /// Asynchronously reacts to an event by producing a sequence of commands to handle.
    /// </summary>
    /// <param name="event">The instigating event.</param>
    /// <param name="metadata">A set of metadata that was saved with the event, if any.</param>
    /// <param name="cancellationToken">A <see cref="CancellationToken"/> to cancel the asynchronous enumeration.</param>
    /// <returns>An asynchronous sequence of commands.</returns>
    IAsyncEnumerable<TCommand> ReactAsync(TReactionEvent @event, IReadOnlyDictionary<string, object?>? metadata = null, CancellationToken cancellationToken = default) =>
        React(@event).ToAsyncEnumerable();
}

/// <summary>
/// Represents the changes that are made to a projection, but have not been committed yet.
/// </summary>
/// <typeparam name="TState">The type of the state that is produced after committing the changes.</typeparam>
public interface ICommit<TState> {
    /// <summary>
    /// Asynchronously commits the changes made to a projection after applying an event.
    /// </summary>
    /// <param name="cancellationToken">A <see cref="CancellationToken"/> to cancel the asynchronous operation.</param>
    /// <returns>A <see cref="ValueTask"/> that represents the asynchronous operation, which resolves to the new state.</returns>
    ValueTask<TState> CommitAsync(CancellationToken cancellationToken = default);
}

/// <summary>
/// Decides which events are required to progress a state object as a result of executing a command.
/// </summary>
/// <typeparam name="TCommand">The type of the command itself.</typeparam>
/// <typeparam name="TState">The type of the maintained state object.</typeparam>
/// <typeparam name="TEvent">The type of the event(s) that are applicable.</typeparam>
public interface ICommand<in TCommand, in TState, out TEvent>
    where TState : IState<TState, TEvent>
    where TCommand : ICommand<TCommand, TState, TEvent> {
    /// <summary>
    /// Accepts the <paramref name="state"/> to produce a sequence of events that will progress it to a new state.
    /// </summary>
    /// <param name="state">The current state to accept.</param>
    /// <returns>A sequence of events.</returns>
    IEnumerable<TEvent> Progress(TState state) =>
        ProgressAsync(state).ToEnumerable();

    /// <summary>
    /// Accepts the <paramref name="state"/> to produce a sequence of events that will progress it to a new state.
    /// </summary>
    /// <param name="state">The current state to accept.</param>
    /// <param name="cancellationToken">A <see cref="CancellationToken"/> to cancel the asynchronous operation.</param>
    /// <returns>An asynchronous sequence of events.</returns>
    IAsyncEnumerable<TEvent> ProgressAsync(TState state, CancellationToken cancellationToken = default) =>
        Progress(state).ToAsyncEnumerable();

    /// <summary>
    /// Implicitly casts the given <typeparamref name="TCommand"/> to an <see cref="AggregateIdentifier"/>.
    /// </summary>
    /// <param name="instance">The command to cast.</param>
    static abstract implicit operator AggregateIdentifier(TCommand instance);
}

/// <summary>
/// Serializes the given <paramref name="payload"/> to the given <paramref name="destination"/> <see cref="Stream"/>.
/// </summary>
/// <param name="destination">The <see cref="Stream"/> to write the serialized payload to.</param>
/// <param name="payload">The payload to serialize.</param>
public delegate void SerializerDelegate(Stream destination, object payload);

/// <summary>
/// Deserializes the given binary serialized payload.
/// </summary>
/// <param name="source">The <see cref="Stream"/> to read the serialized payload from.</param>
/// <param name="target">The target type to deserialize to.</param>
/// <returns>A <see cref="object"/>.</returns>
delegate object DeserializerDelegate(Stream source, Type target);

interface IUpgradeEvent<in TOldVersion, out TNewVersion> {
    /// <summary>
    /// Upgrades the given <paramref name="event"/> to a <typeparamref name="TNewVersion"/>.
    /// </summary>
    /// <param name="event">The <typeparamref name="TOldVersion"/> to upgrade.</param>
    /// <returns>A <typeparamref name="TNewVersion"/>.</returns>
    TNewVersion Upgrade(TOldVersion @event);
}

[AttributeUsage(AttributeTargets.Class | AttributeTargets.Struct, AllowMultiple = false, Inherited = false)]
public class EventContractAttribute : Attribute {
    readonly string _contractName;
    readonly int _contractVersion;
    readonly string? _ns;
    readonly Func<object, object>? _upgrader;

    /// <summary>
    /// Initializes a new <see cref="EventContractAttribute"/>.
    /// </summary>
    /// <param name="name">The name of the event contract.</param>
    /// <param name="version">The version of the event contract.</param>
    /// <param name="eventUpgrader">A type that implements <see cref="IUpgradeEvent{TOldVersion,TNewVersion}"/> which is to be used to upgrade the attributed event type.</param>
    /// <param name="namespace">An optional namespace to prepend to the contract name. The namespace will be separated from the contract name with a dot.</param>
    public EventContractAttribute(string name, int version = 1, Type? eventUpgrader = null, string? @namespace = null) {
        if (string.IsNullOrWhiteSpace(name)) throw new ArgumentNullException(nameof(name));

        // validate that the eventUpgrader is an implementation of IUpgradeEvent
        var upgrader = (
            from iface in eventUpgrader?.GetInterfaces() ?? Enumerable.Empty<Type>()
            where iface.IsGenericType && iface.GetGenericTypeDefinition() == typeof(IUpgradeEvent<,>)
            let upgradeMethod = iface.GetMethod("Upgrade")
            let instance = Activator.CreateInstance(eventUpgrader!)
            select new Func<object, object>(@event => upgradeMethod!.Invoke(instance, new[] { @event })!)
        ).ToArray();
        if (eventUpgrader != null && !upgrader.Any()) throw new ArgumentOutOfRangeException(nameof(eventUpgrader));

        _contractName = name;
        _contractVersion = version;
        _ns = @namespace;
        _upgrader = upgrader.FirstOrDefault();
    }

    /// <summary>
    /// Attempts to upgrade the given <paramref name="event"/>.
    /// </summary>
    /// <remarks>A given <paramref name="event"/> can only be upgraded if an <see cref="IUpgradeEvent{TOldVersion,TNewVersion}"/> type was passed to the constructor.</remarks>
    /// <param name="event">The event to upgrade.</param>
    /// <param name="upgradedEvent">The resulting upgraded event.</param>
    /// <returns><c>true</c> if the given <paramref name="event"/> was upgraded, otherwise <c>false</c>.</returns>
    public bool TryUpgrade(object @event, out object upgradedEvent) {
        upgradedEvent = default!;
        if (_upgrader == default) return false;
        upgradedEvent = _upgrader(@event);
        return true;
    }

    /// <summary>
    /// Returns the event contract name as it will be used in the event log.
    /// </summary>
    /// <returns>A <see cref="string"/>.</returns>
    public override string ToString() => $"{(!string.IsNullOrWhiteSpace(_ns) ? $"{_ns}." : string.Empty)}{_contractName}@v{_contractVersion}";
}