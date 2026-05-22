namespace Aggregates.Policies;

record struct PolicyTestEvent(int Value);

record struct PolicyTestCommand : ICommand;

// Minimal concrete IPolicy implementation used only as a type parameter marker in
// PolicyHandler<TPolicy, TEvent> — actual policy logic is always faked.
class TestPolicy : IPolicy<PolicyTestEvent> {
    public IAsyncEnumerable<ICommand> ReactAsync(PolicyTestEvent @event, CancellationToken cancellationToken = default) =>
        AsyncEnumerable.Empty<ICommand>();
}
