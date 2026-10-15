namespace Aggregates.Subscriptions;

/// <summary>
/// The open requests of one saga or policy, built from the scheduled entries of the store in commit
/// order. Not thread-safe.
/// </summary>
/// <param name="owners">The subscription ids whose requests are this handler's: its own and its predecessors'.</param>
/// <param name="start">The position the entries are read after; null from the beginning.</param>
/// <param name="deserializeCommand">Deserializes a deferred command (see <see cref="IScheduledStore.DeserializeCommand"/>).</param>
sealed class SchedulerIndex(IReadOnlySet<string> owners, ulong? start, Func<string, ReadOnlyMemory<byte>, object?> deserializeCommand) {
    readonly Dictionary<EventReference, ScheduledRequest> _open = [];
    readonly SortedSet<ScheduledRequest> _byDue = new(Comparer<ScheduledRequest>.Create((x, y) =>
        x.Due != y.Due ? x.Due.CompareTo(y.Due) : x.CommitPosition.CompareTo(y.CommitPosition)));
    readonly SortedDictionary<ulong, ScheduledRequest> _byPosition = [];
    ulong? _lastPosition = start;

    /// <summary>
    /// The number of open requests.
    /// </summary>
    public int Count => _open.Count;

    /// <summary>
    /// The due time of the first open request, or null.
    /// </summary>
    public DateTimeOffset? NextDue => _byDue.Count == 0 ? null : _byDue.Min.Due;

    /// <summary>
    /// The position a restart reads after so that it finds every open request again: before the oldest
    /// open request, or the last position read when none is open; null when that is the beginning.
    /// </summary>
    public ulong? Checkpoint => _byPosition.Count == 0 ? _lastPosition : _byPosition.First().Value.ResumeAfter;

    /// <summary>
    /// Applies the next entry of the store.
    /// </summary>
    /// <param name="entry">The entry.</param>
    /// <param name="requested">The opened request when the result is <see cref="SchedulerChange.Requested"/>.</param>
    public SchedulerChange Apply(ScheduledEntry entry, out ScheduledRequest requested) {
        requested = default;
        if (entry.IsProgress) {
            // Progress at or before the known position (KurrentDB reports 0 when it starts) is no progress.
            if (entry.CommitPosition > (_lastPosition ?? 0))
                _lastPosition = entry.CommitPosition;
            return SchedulerChange.None;
        }

        var resumeAfter = _lastPosition;
        _lastPosition = entry.CommitPosition;
        switch (entry.EventType) {
            case ScheduledFormat.CommandRequestEventType:
                if (!ScheduledFormat.TryReadCommandRequest(entry.Data, out var envelope))
                    return SchedulerChange.Invalid;
                if (!owners.Contains(envelope.Owner) || _open.ContainsKey(entry.Source))
                    return SchedulerChange.None;
                var (command, error) = Deserialize(envelope);
                requested = new ScheduledRequest(entry.Source, envelope.Due, command, error, entry.Metadata, entry.CommitPosition, resumeAfter);
                _open.Add(entry.Source, requested);
                _byDue.Add(requested);
                _byPosition.Add(entry.CommitPosition, requested);
                return SchedulerChange.Requested;
            case ScheduledFormat.DeliveryEventType:
                return EventReference.TryParseLinkData(entry.Data.Span, out var request) && Remove(request)
                    ? SchedulerChange.Delivered
                    : SchedulerChange.None;
            case ScheduledFormat.MarkerEventType:
                return SchedulerChange.Marker;
            default:
                return SchedulerChange.None;
        }
    }

    /// <summary>
    /// The first open request when it is due at <paramref name="now"/>.
    /// </summary>
    public bool TryPeekDue(DateTimeOffset now, out ScheduledRequest request) {
        if (_byDue.Count > 0 && _byDue.Min.Due <= now) {
            request = _byDue.Min;
            return true;
        }
        request = default;
        return false;
    }

    /// <summary>
    /// Closes the request stored at <paramref name="request"/>.
    /// </summary>
    /// <returns><see langword="true"/> when it was open.</returns>
    public bool Remove(EventReference request) {
        if (!_open.Remove(request, out var open))
            return false;
        _byDue.Remove(open);
        _byPosition.Remove(open.CommitPosition);
        return true;
    }

    // Deserialized when read, so a command type that changed is reported at startup instead of when it is due.
    (ICommand? Command, Exception? Error) Deserialize(ScheduledCommandEnvelope envelope) {
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
