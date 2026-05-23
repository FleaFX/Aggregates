# Aggregates

[![Build & test](https://github.com/FleaFX/aggregates/actions/workflows/build.yml/badge.svg)](https://github.com/FleaFX/aggregates/actions/workflows/build.yml)

> **⚠️ Preview:** This is a pre-release version (`1.0.0-preview.1`). The API is stabilising but may still change before 1.0.0. For the previous stable version, see the [`legacy`](https://github.com/FleaFX/aggregates/tree/legacy) branch.

This library provides all the boilerplate code needed to do event sourcing, leaving you with just the core functionality of your domain to write.

> **Note:** This is a ground-up rewrite of the [existing Aggregates library](https://github.com/FleaFX/aggregates/tree/legacy). Both versions will continue to exist alongside each other for the time being.

## Packages

| Package | Description |
|---------|-------------|
| `Aggregates` | Core package: commands, aggregates, unit of work |
| `Aggregates.Subscriptions` | Shared abstractions: `ICheckpointStore`, `ISubscription`, `ISubscriptionFactory` |
| `Aggregates.Sagas` | Saga pattern: stateful event-driven process managers |
| `Aggregates.Policies` | Policy pattern: stateless event-driven command dispatchers |
| `Aggregates.Projections` | Projection pattern: event-driven read-model builders |

Storage integration packages are provided separately and are required to wire everything up:

| Package | Description |
|---------|-------------|
| `Aggregates.KurrentDB` | KurrentDB aggregate persistence |
| `Aggregates.Sagas.KurrentDB` | KurrentDB saga storage + subscriptions |
| `Aggregates.Policies.KurrentDB` | KurrentDB policy subscriptions |
| `Aggregates.Projections.KurrentDB` | KurrentDB projection subscriptions |
| `Aggregates.MSSP` | MSSP aggregate persistence |
| `Aggregates.Sagas.MSSP` | MSSP saga storage + subscriptions |
| `Aggregates.Policies.MSSP` | MSSP policy subscriptions |
| `Aggregates.Projections.MSSP` | MSSP projection subscriptions |
| `Aggregates.Projections.Sql` | SQL projection commits via ADO.NET |

## Getting started

### Functional event sourcing

This library is based on two core functions from functional event sourcing.

The first takes a state and an event, and produces the next state:

```
state → event → state
```

Since an event is an irrejectable fact that has already happened, this function **must always produce a new state** — no exceptions allowed.

The second takes a state and a command, and produces a sequence of events:

```
state → command → events[]
```

A command is an intent, not a guarantee. This function **is allowed to fail** — this is where you validate input and enforce your domain rules. The produced sequence may contain one event, multiple events, or none at all.

### IState

Implement `IState<TState, TEvent>` on your state object:

```csharp
record ShoppingCartState(ImmutableDictionary<string, int> Items)
    : IState<ShoppingCartState, IShoppingCartEvent> {

    public static ShoppingCartState Initial => new(ImmutableDictionary<string, int>.Empty);

    public ShoppingCartState Apply(IShoppingCartEvent @event) => @event switch {
        ItemAdded e   => this with { Items = Items.SetItem(e.ItemId, Items.GetValueOrDefault(e.ItemId) + e.Quantity) },
        ItemRemoved e => this with { Items = Items.SetItem(e.ItemId, Items[e.ItemId] - e.Quantity) },
        _             => this
    };
}
```

### ICommand

Implement `ICommand<TState, TEvent>` on your commands. The `Id` property identifies the target aggregate; `ProgressAsync` validates the current state and yields the resulting events:

```csharp
record AddItem(AggregateIdentifier Id, string ItemId, int Quantity)
    : ICommand<ShoppingCartState, IShoppingCartEvent> {

    public async IAsyncEnumerable<IShoppingCartEvent> ProgressAsync(
        ShoppingCartState state,
        [EnumeratorCancellation] CancellationToken cancellationToken = default) {

        if (Quantity <= 0) throw new ArgumentOutOfRangeException(nameof(Quantity));
        yield return new ItemAdded(ItemId, Quantity);
    }
}
```

### Handling commands

Inject `ICommandHandler<TCommand>` wherever you need it:

```csharp
class ShoppingCartController(ICommandHandler<AddItem> handler) : ControllerBase {
    [HttpPost("{id:guid}")]
    public async Task<IActionResult> Post(Guid id, [FromBody] AddItemRequest request, CancellationToken ct) {
        await handler.HandleAsync(new AddItem(id.ToString(), request.ItemId, request.Quantity), ct);
        return Ok();
    }
}
```

### Wiring up

**KurrentDB:**
```csharp
services
    .AddAggregates(o => o.ScanAssemblies(typeof(AddItem).Assembly))
    .AddKurrentDb(o => {
        o.Serialize   = e => new SerializedEvent(e.GetType().Name, JsonSerializer.SerializeToUtf8Bytes(e));
        o.Deserialize = (type, data) => /* your deserializer */;
    });
```

**MSSP:**
```csharp
services
    .AddAggregates(o => o.ScanAssemblies(typeof(AddItem).Assembly))
    .AddMssp(o => {
        o.Serialize   = e => new EventData(e.GetType().Name, JsonSerializer.SerializeToUtf8Bytes(e));
        o.Deserialize = (type, data) => /* your deserializer */;
    });
```

`ScanAssemblies` discovers all `ICommand<TState, TEvent>` implementations in the given assemblies and registers a handler for each.

---

## Sagas

A saga is a stateful process manager that reacts to a stream of domain events by dispatching commands. Unlike a policy, a saga maintains its own state across multiple events.

### ISaga

Implement `ISaga<TSagaState, TEvent>` to define the reaction logic. The saga class is pure — it receives the current state and the triggering event, and yields zero or more commands:

```csharp
[SagaContract("OrderFulfillment")]
class OrderFulfillmentSaga : ISaga<OrderFulfillmentState, IOrderEvent> {

    public async IAsyncEnumerable<ICommand> ReactAsync(
        OrderFulfillmentState state,
        IOrderEvent @event,
        [EnumeratorCancellation] CancellationToken cancellationToken = default) {

        if (@event is OrderPlaced placed && !state.IsShipped)
            yield return new ShipOrder(placed.OrderId);
    }
}
```

The saga state uses the same `IState<TState, TEvent>` pattern as aggregates:

```csharp
record OrderFulfillmentState(bool IsShipped) : IState<OrderFulfillmentState, IOrderEvent> {
    public static OrderFulfillmentState Initial => new(false);

    public OrderFulfillmentState Apply(IOrderEvent @event) => @event switch {
        OrderShipped => this with { IsShipped = true },
        _            => this
    };
}
```

### SagaContractAttribute

Decorate your saga class with `[SagaContract]` to assign it a stable identity for checkpointing and versioning:

```csharp
[SagaContract("OrderFulfillment", version: 1, namespace: "Orders", startFromEnd: false)]
class OrderFulfillmentSaga : ISaga<OrderFulfillmentState, IOrderEvent> { ... }
```

`startFromEnd: true` causes the saga to start consuming only new events on its first run, skipping the existing history.

### Wiring up sagas

**KurrentDB:**
```csharp
services
    .AddAggregates(o => o.ScanAssemblies(typeof(ShipOrder).Assembly))
    .AddKurrentDb(o => { /* serialization */ });

services
    .AddAggregates()
    .AddSagas(o => o
        .ScanAssemblies(typeof(OrderFulfillmentSaga).Assembly)
        .WithResolver<IOrderEvent>(e => e is OrderPlaced p
            ? [new AggregateIdentifier(p.OrderId)]
            : []))
    .AddKurrentDb();
```

**MSSP:**
```csharp
services
    .AddAggregates(o => o.ScanAssemblies(typeof(ShipOrder).Assembly))
    .AddMssp(o => { /* serialization */ });

services
    .AddAggregates()
    .AddSagas(o => o
        .ScanAssemblies(typeof(OrderFulfillmentSaga).Assembly)
        .WithResolver<IOrderEvent>(e => e is OrderPlaced p
            ? [new AggregateIdentifier(p.OrderId)]
            : []))
    .AddMssp();
```

`WithResolver` determines which saga instance(s) should handle a given event. The subscription hosted service is registered automatically for every saga that has a resolver.

---

## Policies

A policy is a stateless event-driven reaction: it receives an event and dispatches zero or more commands, with no state of its own.

### IPolicy

Implement `IPolicy<TEvent>` to define the reaction logic:

```csharp
[PolicyContract("SendWelcomeEmail")]
class SendWelcomeEmailPolicy : IPolicy<UserRegistered> {

    public async IAsyncEnumerable<ICommand> ReactAsync(
        UserRegistered @event,
        [EnumeratorCancellation] CancellationToken cancellationToken = default) {

        yield return new SendEmail(@event.UserId, "Welcome!");
    }
}
```

### Wiring up policies

**KurrentDB:**
```csharp
services
    .AddAggregates(o => o.ScanAssemblies(typeof(SendEmail).Assembly))
    .AddKurrentDb(o => { /* serialization */ });

services
    .AddAggregates()
    .AddPolicies(o => o.ScanAssemblies(typeof(SendWelcomeEmailPolicy).Assembly))
    .AddKurrentDb();
```

**MSSP:**
```csharp
services
    .AddAggregates(o => o.ScanAssemblies(typeof(SendEmail).Assembly))
    .AddMssp(o => { /* serialization */ });

services
    .AddAggregates()
    .AddPolicies(o => o.ScanAssemblies(typeof(SendWelcomeEmailPolicy).Assembly))
    .AddMssp();
```

The subscription hosted service is registered automatically for every scanned policy.

---

## Projections

A projection listens to a stream of domain events and builds a read model by writing to an external store (e.g. a SQL database). Unlike sagas and policies, projections do not dispatch commands — they produce writes.

### IProjection

Implement `IProjection<TEvent>` to define the projection logic. The method returns an `ICommit` that describes the pending writes; the infrastructure executes it after the method returns:

```csharp
[ProjectionContract("Orders")]
class OrdersProjection(IDbConnectionFactory db) : IProjection<IOrdersProjectionEvent> {

    public ValueTask<ICommit> ProjectAsync(
        IOrdersProjectionEvent @event,
        CancellationToken cancellationToken = default) =>

        ValueTask.FromResult<ICommit>(@event switch {
            OrderPlaced e  => Commit.Create().UseSql(db)
                                .Query("INSERT INTO orders (id, date) VALUES (@Id, @Date)",
                                       new { e.Id, e.Date }),
            OrderShipped e => Commit.Create().UseSql(db)
                                .Query("UPDATE orders SET shipped = 1 WHERE id = @Id",
                                       new { e.Id }),
            _              => Commit.Create()
        });
}
```

### Wiring up projections

**KurrentDB:**
```csharp
services
    .AddProjections(o => o.ScanAssemblies(typeof(OrdersProjection).Assembly))
    .AddKurrentDb();
```

**MSSP:**
```csharp
services
    .AddProjections(o => o.ScanAssemblies(typeof(OrdersProjection).Assembly))
    .AddMssp();
```

`ScanAssemblies` discovers all `IProjection<TEvent>` implementations and registers a subscription hosted service for each. `AddKurrentDb`/`AddMssp` supplies the `ISubscriptionFactory` and `ICheckpointStore`.
