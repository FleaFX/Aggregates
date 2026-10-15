namespace Aggregates.Sagas;

/// <summary>
/// A deadline a saga requests: yield it with <see cref="At"/> from <see cref="ISaga{TSagaState,TEvent}.ReactAsync"/>
/// or <see cref="ISaga{TSagaState,TEvent,TDeadline}.ReactToDeadlineAsync"/>, among the commands. It is stored
/// with the saga's state, and once <see cref="Due"/> has passed, <see cref="Payload"/> is delivered back to
/// the same saga instance through <see cref="ISaga{TSagaState,TEvent,TDeadline}.ReactToDeadlineAsync"/>,
/// with the saga's state at that moment. The deadline doesn't change that state.
/// </summary>
/// <remarks>
/// Use a deadline when the saga decides at the due time what to do: when the decision combines several
/// aggregates, or when the command is chosen only then. When the command is known at the request, defer
/// it with <see cref="DeferredCommandExtensions.DeferUntil"/> instead. The payload waits in the store
/// until it is due, so the storage integration's <c>Serialize</c> and <c>Deserialize</c> must know its
/// type under a stable name. Only sagas that implement <see cref="ISaga{TSagaState,TEvent,TDeadline}"/>
/// request deadlines.
/// </remarks>
public sealed record Deadline : ICommand {
    Deadline(DateTimeOffset due, object payload) {
        Due = due;
        Payload = payload;
    }

    /// <summary>
    /// A deadline that delivers <paramref name="payload"/> back to the saga once <paramref name="due"/> has passed.
    /// </summary>
    /// <param name="due">When the deadline is delivered. A time in the past delivers it right away.</param>
    /// <param name="payload">What the saga receives: an instance of the <c>TDeadline</c> of its <see cref="ISaga{TSagaState,TEvent,TDeadline}"/>.</param>
    /// <exception cref="ArgumentNullException"><paramref name="payload"/> is <see langword="null"/>.</exception>
    /// <exception cref="ArgumentException"><paramref name="payload"/> is a <see cref="Deadline"/> or a <see cref="DeferredCommand"/>.</exception>
    public static Deadline At(DateTimeOffset due, object payload) {
        ArgumentNullException.ThrowIfNull(payload);
        if (payload is Deadline or DeferredCommand)
            throw new ArgumentException("A deadline's payload can't be another deadline or a deferred command.", nameof(payload));
        return new Deadline(due, payload);
    }

    /// <summary>
    /// When the deadline is delivered.
    /// </summary>
    public DateTimeOffset Due { get; }

    /// <summary>
    /// The payload delivered back to the saga at <see cref="Due"/>.
    /// </summary>
    public object Payload { get; }
}
