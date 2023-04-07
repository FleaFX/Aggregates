using Aggregates.Util;
using System.Collections.Immutable;

namespace Aggregates;

/// <summary>
/// Asynchronously commits the changes made in the given <see cref="UnitOfWork"/>.
/// </summary>
/// <param name="unitOfWork">The <see cref="UnitOfWork"/> that contains the changes to commit.</param>
/// <returns>A <see cref="ValueTask"/> that represents the asynchronous commit operation.</returns>
delegate ValueTask CommitDelegate(UnitOfWork unitOfWork);

sealed class UnitOfWork {
    readonly Dictionary<AggregateIdentifier, object> _aggregates = new();

    /// <summary>
    /// Retrieves the <see cref="Aggregate"/> associated with the given <paramref name="identifier"/>.
    /// </summary>
    /// <param name="identifier">Uniquely identifies the <see cref="Aggregate"/> to retrieve.</param>
    /// <returns>An <see cref="Aggregate"/>, or <c>null</c> if it was not found.</returns>
    public Aggregate? Get(AggregateIdentifier identifier) =>
        _aggregates.TryGetValue(identifier, out var aggregate) ? (Aggregate)aggregate : null;

    /// <summary>
    /// Attaches the given <paramref name="aggregate"/> to the unit of work.
    /// </summary>
    /// <param name="aggregate">The <see cref="Aggregate"/> to attach.</param>
    /// <exception cref="InvalidOperationException">Thrown when attaching an <see cref="Aggregate"/> that is already attached.</exception>
    public void Attach(Aggregate aggregate) {
        if (Get(aggregate.Identifier) is not null) throw new InvalidOperationException();
        _aggregates.Add(aggregate.Identifier, aggregate);
    }

    /// <summary>
    /// Gets the <see cref="Aggregate"/> that was changed, if any.
    /// </summary>
    /// <remarks>
    /// Changing more than one aggregate in a single unit of work is not supported. An exception will be thrown if this is the case.
    /// </remarks>
    /// <returns>An <see cref="Aggregate"/>, or <c>null</c> if no aggregates were changed.</returns>
    public Aggregate? GetChanged() {
        var changed = _aggregates.Values.SingleOrDefault(a => ((Aggregate)a).AggregateRoot.GetChanges().Any());
        if (Aggregate.None.Equals(changed)) return null;
        return (Aggregate)changed!;
    }
}

sealed class UnitOfWorkScope : IAsyncDisposable {
    readonly UnitOfWork _unitOfWork;
    readonly CommitDelegate _onCommit;

    bool _completed;

    /// <summary>
    /// Initializes a new <see cref="UnitOfWorkScope"/>
    /// </summary>
    /// <param name="unitOfWork">The unit of work to scope.</param>
    /// <param name="onCommit">The <see cref="CommitDelegate"/> to call when committing changes.</param>
    public UnitOfWorkScope(UnitOfWork unitOfWork, CommitDelegate onCommit) {
        _unitOfWork = unitOfWork;
        _onCommit = onCommit;

        Scopes = Scopes.Push(this);
    }

    /// <summary>
    /// When called, marks the scope as ready to commit at dispose time.
    /// </summary>
    public void Complete() => _completed = true;

    static readonly string ThreadId = Guid.NewGuid().ToString("N");
    static ImmutableStack<UnitOfWorkScope> Scopes {
        get => CallContext<ImmutableStack<UnitOfWorkScope>>.LogicalGetData(ThreadId) ?? ImmutableStack.Create<UnitOfWorkScope>();
        set => CallContext<ImmutableStack<UnitOfWorkScope>>.LogicalSetData(ThreadId, value);
    }

    /// <summary>
    /// Performs application-defined tasks associated with freeing, releasing, or resetting unmanaged resources asynchronously.
    /// </summary>
    /// <returns>A <see cref="ValueTask"/> that represents the asynchronous dispose operation.</returns>
    public ValueTask DisposeAsync() {
        Scopes = Scopes.Pop(out var scope);
        return scope._completed ?
            scope._onCommit(_unitOfWork) :
            ValueTask.CompletedTask;
    }
}