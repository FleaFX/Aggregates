# Aggregates

This library provides all the boilerplate code needed to do event sourcing, leaving you with just the core functionality of your domain to write.

## Packages

Even though I've just implemented the basics that fulfill my own needs, should you wish, you can download a couple of nuget packages.

* The core package: [Aggregates](https://www.nuget.org/packages/Aggregates)

Then you'll need some add-on packages as well for storage and serialization.

* Integration with [EventStoreDB](https://github.com/EventStore/EventStore): [Aggregates.EventStoreDB](https://www.nuget.org/packages/Aggregates.EventStoreDB)
* Serialization using JSON: [Aggregates.Json](https://www.nuget.org/packages/Aggregates.Json)
* or Protobuf: [Aggregates.Protobuf](https://www.nuget.org/packages/Aggregates.Protobuf)

## Getting started

### Functional event sourcing in theory

I will assume that you have knowledge about event sourcing. I will keep this preliminary explanation brief.

Functional event sourcing is always based around two functions:

* Progressing from one state to the next by applying an event. Note that, since an event is an irrejectable fact, this function MUST produce a state with no questions asked. This function typically has the following signature:

```
state -> event -> state
```


* Then you'll have a function for handling commands. It is allowed to reject a command (e.g.by throwing an exception). The produced sequence of events may contain a single event, multiple events or none at all. This function typically has the following signature:

```
state -> command -> events[]
```

### Implementation

Aggregates implements these two functions through two interfaces that you'll implement in your domain: `IState` and `ICommand`. Let's go over them first and then implement the classic shopping cart example.

`IState` expects you to implement a method and a property:

```csharp
TState Apply(TEvent @event);

TState Initial { get; }
```

`Apply` is an instance method and so you can consider the current instance the state parameter of the function signature mentioned above. `Initial` provides the applicator function with a starting point when applying its very first event.

Then there's `ICommand`. That one needs you to implement this method:

```csharp
IEnumerable<TEvent> Progress(TState state);
```

### Example

Let's implement the classic shopping cart example. I suppose you'll need at least these two events:

```csharp

public interface IShoppingCartEvent { }

[EventContract(name: nameof(ItemAdded), version: 1, @namespace: "Example")]
public readonly record struct ItemAdded(
    string ItemId,
    int Quantity
) : IShoppingCartEvent

[EventContract(name: nameof(ItemRemoved))]
public readonly record struct ItemRemoved(
    string ItemId,
    int Quantity
) : IShoppingCartEvent

```
I've added a marker interface for events that are handled by our shopping cart so that I can pattern match on this later on. The events are attributed with `EventContract`. This governs the type of the event when storing it in EventStoreDB by giving it a name, a version and a namespace. The `ItemRemoved` event shows that the latter two are optional. In this example, the event types would end up as 'Example.ItemAdded@v1' and 'ItemRemoved@v1'. `ItemRemoved` does not have the namespace prefix, but it still has the version suffix as this defaults to 1. These versions are used when upgrading, but I'll leave this out of this introduction.

You can now use these events in the state object:
```csharp
using System.Collections.Immutable;

public readonly record struct ShoppingCart(Guid Id, ImmutableDictionary<string, int> Items) : IState<ShoppingCart, IShoppingCartEvent> {
    public static ShoppingCart Initial => new(Guid.NewGuid(), ImmutableDictionary<string, int>.Empty);

    public ShoppingCart Apply(IShoppingCartEvent @event) =>
        @event switch {
            ItemAdded added =>
                this with { Items =
                    (this.Items.TryGetValue(@event.ItemId, out var current) ?
                        this.Items.SetItem(@event.ItemId, current + @event.Quantity) :
                        this.Items.Add(@event.ItemId, @event.Quantity)
                    )
                },

            ItemRemoved removed =>
                this.Items.TryGetValue(@event.ItemId, out var current) ?
                    this with { Items =
                        current == @event.Quantity ?
                            this.Items.Remove(@event.ItemId) :
                            this.Items.SetItem(@event.ItemId, current - @event.Quantity)
                    } :
                    this,

            _ => throw new InvalidOperationException()
        }
}
```
Here you see that the `Apply` function pattern matches on the type of the event to decide what to do and consequently produce the new state.

Next we have two commands to add to and remove from the shopping cart:
```csharp
public readonly record struct AddItem(
    Guid ShoppingCartId,
    string ItemId,
    int Quantity
) : ICommand<AddItem, ShoppingCart, IShoppingCartEvent> {
    public IEnumerable<IShoppingCartEvent> Progress(ShoppingCart state) {
        if (string.IsNullOrWhiteSpace(ItemId)) throw new ArgumentNullException(nameof(ItemId));
        if (Quantity <= 0) throw new ArgumentOutOfRangeException(nameof(Quantity));

        yield return new ItemAdded(ItemId, Quantity);
    }

    public static implicit operator AggregateIdentifier(AddItem instance) => new AggregateIdentifier($"{ShoppingCartId:N}");
}

public readonly record struct RemoveItem(
    Guid ShoppingCartId,
    string ItemId,
    int Quantity
) : ICommand<RemoveItem, ShoppingCart, IShoppingCartEvent> {
    public IEnumerable<IShoppingCartEvent> Progress(ShoppingCart state) {
        if (string.IsNullOrWhiteSpace(ItemId)) throw new ArgumentNullException(nameof(ItemId));
        if (Quantity <= 0) throw new ArgumentOutOfRangeException(nameof(Quantity));
        if (!state.Items.TryGetItem(ItemId, out var current) || current < Quantity) throw new InvalidOperationException();

        yield return new ItemRemoved(ItemId, Quantity);
    }

    public static implicit operator AggregateIdentifier(AddItem instance) => new AggregateIdentifier($"{ShoppingCartId:N}");
}
```
The `Progress` method receives the current state and uses it to do some validations and guard domain rules, e.g. you can't remove an item that isn't in the cart of remove more items than the current amount in the cart. If all is well, it yields the events that are needed to progress to the next state. Each command has one more thing, the cast operator to `AggregateIdentifier`. This is a technicality that is necessary to know which aggregate object to apply the command to. You wouldn't want to get shopping carts from different users mixed up.

That is it as the domain is concerned. You just need a way to put it to work now, which comes in the form of the `ICommandHandler` interface. I'll assume some kind of API controller here, but that's just for the purpose of demonstration. Use them wherever you need.
```csharp

public class ShoppingCartController(
    // initialize these from the constructor:
    readonly ICommandHandler<AddItem, ShoppingCart, IShoppingCartEvent> _addItemHandler;
    readonly ICommandHandler<RemoveItem, ShoppingCart, IShoppingCartEvent> _removeItemHandler;

    [HttpPost]
    [Route("{shoppingCartId:guid}")]
    public async Task<IActionResult> Post(Guid shoppingCartId, [FromBody] AddItem command) {
        await _addItemHandler.Handle(command with { ShoppingCartId = shoppingCartId });
        return Created();
    }

    [HttpDelete]
    [Route("{shoppingCartId:Guid}")]
    public async Task<IActionResult> Delete(Guid shoppingCartId, [FromBody] RemoveItem command) {
        await _removeHandler.Handle(command with { ShoppingCartId = shoppingCartId });
        return Ok();
    }
)

```

All that's left is to wire up all of the infrastructure. Assuming you have a `IServiceCollection` to work with, you can put the following lines in your bootstrapping code:

```csharp
services.UseAggregates(options => {
    options.UseEventStoreDB("put your connection string here some way or another...");

    // and either one of the following
    options.UseJson();
    options.UseProtobuf();
});
```