using System.Collections.Immutable;

namespace Aggregates;

/// <summary>
/// Asynchronously persists the changes tracked by the given <see cref="UnitOfWork"/>.
/// </summary>
/// <param name="unitOfWork">The unit of work containing the changes to commit.</param>
public delegate ValueTask CommitDelegate(UnitOfWork unitOfWork);

/// <summary>
/// Tracks aggregates loaded within a single operation and identifies which one has pending changes.
/// </summary>
public sealed class UnitOfWork {
    readonly Dictionary<AggregateIdentifier, Aggregate> _aggregates = new();

    /// <summary>
    /// Retrieves the <see cref="Aggregate"/> associated with <paramref name="identifier"/>, or
    /// <see langword="null"/> if it has not been attached.
    /// </summary>
    /// <param name="identifier">The identifier to look up.</param>
    public Aggregate? Get(AggregateIdentifier identifier) =>
        _aggregates.TryGetValue(identifier, out var aggregate) ? aggregate : null;

    /// <summary>
    /// Attaches <paramref name="aggregate"/> to this unit of work.
    /// </summary>
    /// <param name="aggregate">The aggregate to attach.</param>
    /// <exception cref="InvalidOperationException">Thrown when the aggregate is already attached.</exception>
    public void Attach(Aggregate aggregate) {
        if (Get(aggregate.Identifier) is not null) throw new InvalidOperationException($"Aggregate '{aggregate.Identifier.Value}' is already attached to this unit of work.");
        _aggregates.Add(aggregate.Identifier, aggregate);
    }

    /// <summary>
    /// Removes all attached aggregates. Any uncommitted changes are discarded.
    /// </summary>
    public void Clear() => _aggregates.Clear();

    /// <summary>
    /// Returns the single aggregate that has pending changes, or <see langword="null"/> if none do.
    /// </summary>
    /// <remarks>
    /// Changing more than one aggregate in a single unit of work is not supported.
    /// </remarks>
    /// <exception cref="InvalidOperationException">Thrown when more than one aggregate has pending changes.</exception>
    public Aggregate? GetChanged() {
        var changed = _aggregates.Values.SingleOrDefault(a => a.AggregateRoot.GetChanges().Any());
        return Aggregate.None.Equals(changed) ? null : changed;
    }
}

/// <summary>
/// Scopes a <see cref="UnitOfWork"/> to the current async context. Call <see cref="Complete"/> before
/// disposing to trigger the commit; otherwise changes are discarded.
/// </summary>
public sealed class UnitOfWorkScope : IAsyncDisposable {
    static readonly AsyncLocal<ImmutableStack<UnitOfWorkScope>> _scopes = new();

    static ImmutableStack<UnitOfWorkScope> Scopes {
        get => _scopes.Value ?? ImmutableStack<UnitOfWorkScope>.Empty;
        set => _scopes.Value = value;
    }

    readonly UnitOfWork _unitOfWork;
    readonly CommitDelegate _onCommit;
    bool _completed;

    /// <summary>
    /// Initializes a new <see cref="UnitOfWorkScope"/> and pushes it onto the ambient scope stack.
    /// </summary>
    /// <param name="unitOfWork">The unit of work to scope.</param>
    /// <param name="onCommit">The delegate to call when committing.</param>
    public UnitOfWorkScope(UnitOfWork unitOfWork, CommitDelegate onCommit) {
        _unitOfWork = unitOfWork;
        _onCommit = onCommit;
        Scopes = Scopes.Push(this);
    }

    /// <summary>
    /// Marks this scope as ready to commit on disposal.
    /// </summary>
    public void Complete() => _completed = true;

    /// <summary>
    /// The <see cref="UnitOfWork"/> tracked by this scope.
    /// </summary>
    public UnitOfWork UnitOfWork => _unitOfWork;

    /// <summary>
    /// Returns the current ambient <see cref="UnitOfWorkScope"/>, or <see langword="null"/> if none is active.
    /// </summary>
    public static UnitOfWorkScope? Current => Scopes.IsEmpty ? null : Scopes.Peek();

    /// <inheritdoc/>
    public ValueTask DisposeAsync() {
        // Pop synchronously in the caller's execution context so the AsyncLocal change
        // is visible to the outer scope. An async DisposeAsync would isolate the change.
        Scopes = Scopes.Pop(out var scope);
        if (!scope._completed) {
            scope._unitOfWork.Clear();
            return ValueTask.CompletedTask;
        }
        return CommitAndClearAsync(scope);
    }

    static async ValueTask CommitAndClearAsync(UnitOfWorkScope scope) {
        try {
            await scope._onCommit(scope._unitOfWork);
        } finally {
            // Always clear — even when the commit delegate throws — so a retry
            // attempt starts with a clean unit of work rather than stale state.
            scope._unitOfWork.Clear();
        }
    }
}
