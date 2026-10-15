using System.Collections.Concurrent;
using System.Reflection;

namespace Aggregates.Sagas;

/// <summary>
/// The deadlines of saga classes: which payload type a saga receives, and how its
/// <see cref="ISaga{TSagaState,TEvent,TDeadline}.ReactToDeadlineAsync"/> is called.
/// </summary>
static class SagaDeadlines {
    static readonly ConcurrentDictionary<Type, Type?> PayloadTypes = new();

    /// <summary>
    /// The <see cref="ISaga{TSagaState,TEvent,TDeadline}"/> interfaces <paramref name="sagaType"/> implements.
    /// </summary>
    /// <param name="sagaType">The saga class.</param>
    public static Type[] InterfacesOf(Type sagaType) =>
        [.. sagaType.GetInterfaces().Where(@interface => @interface.IsGenericType && @interface.GetGenericTypeDefinition() == typeof(ISaga<,,>))];

    /// <summary>
    /// Checks that <paramref name="sagaType"/> can receive a deadline with <paramref name="payload"/>, so that
    /// a request that could never be delivered fails when the saga makes it.
    /// </summary>
    /// <param name="sagaType">The saga class that requests the deadline.</param>
    /// <param name="payload">The payload of the deadline.</param>
    /// <returns><paramref name="payload"/>.</returns>
    /// <exception cref="InvalidOperationException">
    /// The saga doesn't implement <see cref="ISaga{TSagaState,TEvent,TDeadline}"/>, or <paramref name="payload"/>
    /// is not its <c>TDeadline</c>.
    /// </exception>
    public static object Check(Type sagaType, object payload) =>
        PayloadTypes.GetOrAdd(sagaType, type => InterfacesOf(type).FirstOrDefault()?.GetGenericArguments()[2]) switch {
            null => throw new InvalidOperationException(
                $"{sagaType.Name} requests a deadline, but it doesn't implement ISaga<TSagaState, TEvent, TDeadline>, so the deadline could never be delivered."),
            var payloadType when !payloadType.IsInstanceOfType(payload) => throw new InvalidOperationException(
                $"{sagaType.Name} requests a deadline with a {payload.GetType().Name}, but it receives deadlines of type {payloadType.Name}."),
            _ => payload
        };

    /// <summary>
    /// Calls <see cref="ISaga{TSagaState,TEvent,TDeadline}.ReactToDeadlineAsync"/> of <paramref name="saga"/>.
    /// </summary>
    /// <param name="saga">The saga.</param>
    /// <param name="state">The current state of the saga.</param>
    /// <param name="deadline">The payload of the deadline.</param>
    /// <param name="cancellationToken">A cancellation token.</param>
    /// <exception cref="InvalidOperationException">
    /// The saga doesn't implement <see cref="ISaga{TSagaState,TEvent,TDeadline}"/>, or <paramref name="deadline"/>
    /// is not its <c>TDeadline</c>.
    /// </exception>
    public static IAsyncEnumerable<ICommand> ReactAsync<TSagaState, TEvent>(ISaga<TSagaState, TEvent> saga, TSagaState state, object deadline, CancellationToken cancellationToken)
        where TSagaState : IState<TSagaState, TEvent> =>
        Reactions<TSagaState, TEvent>.Of(saga.GetType())(saga, state, deadline, cancellationToken);

    /// <summary>
    /// Calls the saga with the typed payload.
    /// </summary>
    static IAsyncEnumerable<ICommand> Invoke<TSagaState, TEvent, TDeadline>(ISaga<TSagaState, TEvent> saga, TSagaState state, object deadline, CancellationToken cancellationToken)
        where TSagaState : IState<TSagaState, TEvent> =>
        deadline is TDeadline typed
            ? ((ISaga<TSagaState, TEvent, TDeadline>)saga).ReactToDeadlineAsync(state, typed, cancellationToken)
            : throw new InvalidOperationException(
                $"{saga.GetType().Name} receives deadlines of type {typeof(TDeadline).Name}, but got a {deadline.GetType().Name}.");

    /// <summary>
    /// The calls of <see cref="Invoke{TSagaState,TEvent,TDeadline}"/> per saga class, for sagas with state
    /// <typeparamref name="TSagaState"/> and events <typeparamref name="TEvent"/>.
    /// </summary>
    static class Reactions<TSagaState, TEvent>
        where TSagaState : IState<TSagaState, TEvent> {
        static readonly ConcurrentDictionary<Type, Func<ISaga<TSagaState, TEvent>, TSagaState, object, CancellationToken, IAsyncEnumerable<ICommand>>> Calls = new();

        /// <summary>
        /// The call for <paramref name="sagaType"/>.
        /// </summary>
        public static Func<ISaga<TSagaState, TEvent>, TSagaState, object, CancellationToken, IAsyncEnumerable<ICommand>> Of(Type sagaType) =>
            Calls.GetOrAdd(sagaType, Create);

        static Func<ISaga<TSagaState, TEvent>, TSagaState, object, CancellationToken, IAsyncEnumerable<ICommand>> Create(Type sagaType) {
            var payloadType = InterfacesOf(sagaType)
                .FirstOrDefault(@interface => @interface.GetGenericArguments()[0] == typeof(TSagaState) && @interface.GetGenericArguments()[1] == typeof(TEvent))?
                .GetGenericArguments()[2]
                ?? throw new InvalidOperationException(
                    $"{sagaType.Name} received a deadline, but it doesn't implement ISaga<{typeof(TSagaState).Name}, {typeof(TEvent).Name}, TDeadline>.");
            return typeof(SagaDeadlines).GetMethod(nameof(Invoke), BindingFlags.NonPublic | BindingFlags.Static)!
                .MakeGenericMethod(typeof(TSagaState), typeof(TEvent), payloadType)
                .CreateDelegate<Func<ISaga<TSagaState, TEvent>, TSagaState, object, CancellationToken, IAsyncEnumerable<ICommand>>>();
        }
    }
}
