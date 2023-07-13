using Aggregates.Util;
using EventStore.Client;
using System.Collections.Immutable;
using Aggregates.Extensions;

namespace Aggregates.EventStoreDB.Util; 

class LinkEventScope : IDisposable {
    /// <summary>
    /// Gets the <see cref="ResolvedEvent"/> to link to.
    /// </summary>
    public ResolvedEvent LinkEvent { get; }

    /// <summary>
    /// Initializes a new <see cref="LinkEventScope"/>.
    /// </summary>
    /// <param name="linkEvent">The <see cref="ResolvedEvent"/> to hold in scope.</param>
    public LinkEventScope(ResolvedEvent linkEvent) {
        LinkEvent = linkEvent;

        Scopes = Scopes.Push(this);
    }

    /// <summary>
    /// Gets the current <see cref="LinkEventScope"/>.
    /// </summary>
    public static LinkEventScope? Current => Scopes.TryPeek();

    static readonly string ThreadId = Guid.NewGuid().ToString("N");
    static ImmutableStack<LinkEventScope> Scopes {
        get => CallContext<ImmutableStack<LinkEventScope>>.LogicalGetData(ThreadId) ?? ImmutableStack.Create<LinkEventScope>();
        set => CallContext<ImmutableStack<LinkEventScope>>.LogicalSetData(ThreadId, value);
    }

    /// <summary>Performs application-defined tasks associated with freeing, releasing, or resetting unmanaged resources.</summary>
    public void Dispose() =>
        Scopes = Scopes.Pop(out _);
}