using System.Runtime.CompilerServices;

namespace Aggregates.Testing;

// A small order domain shared by the integration scenarios. Projections, policies and sagas
// live next to the tests that use them, so each test registers exactly its own handlers.

/// <summary>
/// The event types of the order domain.
/// </summary>
static class Orders {
    /// <summary>
    /// All order event types, for <see cref="TestHostOptions.Events"/>.
    /// </summary>
    public static readonly Type[] EventTypes = [typeof(OrderPlaced), typeof(OrderShipped)];
}

/// <summary>
/// Marker for the events of an order.
/// </summary>
interface IOrderEvent;

/// <summary>
/// An order was placed.
/// </summary>
[EventContract("OrderPlaced", @namespace: "IntegrationTests")]
sealed record OrderPlaced(string OrderId, string Customer) : IOrderEvent;

/// <summary>
/// An order was shipped.
/// </summary>
[EventContract("OrderShipped", @namespace: "IntegrationTests")]
sealed record OrderShipped(string OrderId) : IOrderEvent;

/// <summary>
/// The state of an order.
/// </summary>
sealed record OrderState(bool Placed, bool Shipped) : IState<OrderState, IOrderEvent> {
    /// <inheritdoc/>
    public static OrderState Initial => new(false, false);

    /// <inheritdoc/>
    public OrderState Apply(IOrderEvent @event) => @event switch {
        OrderPlaced => this with { Placed = true },
        OrderShipped => this with { Shipped = true },
        _ => this
    };
}

/// <summary>
/// Writes the customer of a <see cref="PlaceOrder"/> to the event metadata under <c>customer</c>.
/// </summary>
sealed class CustomerMetadataAttribute() : MetadataAttribute("customer") {
    /// <inheritdoc/>
    public override ValueTask<object?> GetValueAsync(object context, CancellationToken cancellationToken) =>
        ValueTask.FromResult<object?>(((PlaceOrder)context).Customer);
}

/// <summary>
/// Places an order, once.
/// </summary>
[CustomerMetadata]
sealed record PlaceOrder(AggregateIdentifier Id, string Customer) : ICommand<OrderState, IOrderEvent> {
    /// <inheritdoc/>
    public async IAsyncEnumerable<IOrderEvent> ProgressAsync(OrderState state, [EnumeratorCancellation] CancellationToken cancellationToken = default) {
        if (!state.Placed)
            yield return new OrderPlaced(Id.Value, Customer);
    }
}

/// <summary>
/// Ships a placed order, once.
/// </summary>
sealed record ShipOrder(AggregateIdentifier Id) : ICommand<OrderState, IOrderEvent> {
    /// <inheritdoc/>
    public async IAsyncEnumerable<IOrderEvent> ProgressAsync(OrderState state, [EnumeratorCancellation] CancellationToken cancellationToken = default) {
        if (state.Placed && !state.Shipped)
            yield return new OrderShipped(Id.Value);
    }
}
