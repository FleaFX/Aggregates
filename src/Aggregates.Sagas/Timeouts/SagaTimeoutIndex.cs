using Aggregates.Subscriptions;

namespace Aggregates.Sagas;

/// <summary>
/// The open timeouts of one saga, built from the timeout entries of the store in commit order.
/// Not thread-safe.
/// </summary>
/// <param name="owners">The subscription ids whose timeouts are this saga's: its own and its predecessors'.</param>
/// <param name="start">The position the entries are read after; null from the beginning.</param>
/// <param name="deserializeCommand">Deserializes a planned command (see <see cref="ISagaTimeoutStore.DeserializeCommand"/>).</param>
sealed class SagaTimeoutIndex(IReadOnlySet<string> owners, ulong? start, Func<string, ReadOnlyMemory<byte>, object?> deserializeCommand) {
    readonly Dictionary<EventReference, OpenSagaTimeout> _open = [];
    readonly SortedSet<OpenSagaTimeout> _byDue = new(Comparer<OpenSagaTimeout>.Create((x, y) =>
        x.Due != y.Due ? x.Due.CompareTo(y.Due) : x.CommitPosition.CompareTo(y.CommitPosition)));
    readonly SortedDictionary<ulong, OpenSagaTimeout> _byPosition = [];
    ulong? _lastPosition = start;

    /// <summary>
    /// The number of open timeouts.
    /// </summary>
    public int Count => _open.Count;

    /// <summary>
    /// The due time of the first open timeout, or null.
    /// </summary>
    public DateTimeOffset? NextDue => _byDue.Count == 0 ? null : _byDue.Min.Due;

    /// <summary>
    /// The position a restart reads after so that it finds every open timeout again: before the oldest
    /// open request, or the last position read when none is open; null when that is the beginning.
    /// </summary>
    public ulong? Checkpoint => _byPosition.Count == 0 ? _lastPosition : _byPosition.First().Value.ResumeAfter;

    /// <summary>
    /// Applies the next entry of the store.
    /// </summary>
    /// <param name="entry">The entry.</param>
    /// <param name="requested">The opened timeout when the result is <see cref="SagaTimeoutChange.Requested"/>.</param>
    public SagaTimeoutChange Apply(SagaTimeoutEntry entry, out OpenSagaTimeout requested) {
        requested = default;
        if (entry.IsProgress) {
            // Progress at or before the known position (KurrentDB reports 0 when it starts) is no progress.
            if (entry.CommitPosition > (_lastPosition ?? 0))
                _lastPosition = entry.CommitPosition;
            return SagaTimeoutChange.None;
        }

        var resumeAfter = _lastPosition;
        _lastPosition = entry.CommitPosition;
        switch (entry.EventType) {
            case SagaTimeoutFormat.RequestEventType:
                if (!SagaTimeoutFormat.TryReadRequest(entry.Data, out var envelope))
                    return SagaTimeoutChange.Invalid;
                if (!owners.Contains(envelope.Owner) || _open.ContainsKey(entry.Source))
                    return SagaTimeoutChange.None;
                var (command, error) = Deserialize(envelope);
                requested = new OpenSagaTimeout(entry.Source, envelope.Due, command, error, entry.Metadata, entry.CommitPosition, resumeAfter);
                _open.Add(entry.Source, requested);
                _byDue.Add(requested);
                _byPosition.Add(entry.CommitPosition, requested);
                return SagaTimeoutChange.Requested;
            case SagaTimeoutFormat.DeliveryEventType:
                return EventReference.TryParseLinkData(entry.Data.Span, out var request) && Remove(request)
                    ? SagaTimeoutChange.Delivered
                    : SagaTimeoutChange.None;
            case SagaTimeoutFormat.MarkerEventType:
                return SagaTimeoutChange.Marker;
            default:
                return SagaTimeoutChange.None;
        }
    }

    /// <summary>
    /// The first open timeout when it is due at <paramref name="now"/>.
    /// </summary>
    public bool TryPeekDue(DateTimeOffset now, out OpenSagaTimeout timeout) {
        if (_byDue.Count > 0 && _byDue.Min.Due <= now) {
            timeout = _byDue.Min;
            return true;
        }
        timeout = default;
        return false;
    }

    /// <summary>
    /// Closes the timeout requested at <paramref name="request"/>.
    /// </summary>
    /// <returns><see langword="true"/> when it was open.</returns>
    public bool Remove(EventReference request) {
        if (!_open.Remove(request, out var timeout))
            return false;
        _byDue.Remove(timeout);
        _byPosition.Remove(timeout.CommitPosition);
        return true;
    }

    // Deserialized when read, so a command type that changed is reported at startup instead of when it is due.
    (ICommand? Command, Exception? Error) Deserialize(SagaTimeoutEnvelope envelope) {
        try {
            return deserializeCommand(envelope.CommandType, envelope.CommandData) switch {
                ICommand command => (command, null),
                null => (null, new InvalidOperationException($"Deserialize does not know the command type '{envelope.CommandType}'.")),
                var other => (null, new InvalidOperationException($"'{envelope.CommandType}' deserializes to a {other.GetType().Name}, which is not a command.")),
            };
        } catch (Exception exception) when (exception is not OperationCanceledException) {
            return (null, exception);
        }
    }
}
