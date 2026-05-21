# Aggregates

[![Build & test](https://github.com/FleaFX/aggregates/actions/workflows/build.yml/badge.svg)](https://github.com/FleaFX/aggregates/actions/workflows/build.yml)

This library provides all the boilerplate code needed to do event sourcing, leaving you with just the core functionality of your domain to write.

> **Note:** This is a ground-up rewrite of the [existing Aggregates library](https://github.com/FleaFX/aggregates/tree/main). Both versions will continue to exist alongside each other for the time being.

## Packages

* Core package: [Aggregates](https://www.nuget.org/packages/Aggregates) *(coming soon)*

Storage integration packages are provided separately and are required to wire everything up.

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

```csharp
services.AddAggregates(options =>
    options.ScanAssemblies(typeof(AddItem).Assembly));
```

`ScanAssemblies` discovers all `ICommand<TState, TEvent>` implementations in the given assemblies and registers a handler for each. A storage integration package is required to complete the setup.
