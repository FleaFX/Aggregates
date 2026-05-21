namespace Aggregates.Sagas;

record struct TestEvent(int Value);

record TestSagaState(int Total) : IState<TestSagaState, TestEvent> {
    public static TestSagaState Initial => new(0);
    public TestSagaState Apply(TestEvent @event) => new(Total + @event.Value);
}

record struct TestCommand : ICommand;

// Minimal concrete ISaga implementation used only as a type parameter marker in
// SagaHandler<TSaga, TSagaState, TEvent> — actual saga logic is always faked.
class TestSaga : ISaga<TestSagaState, TestEvent> {
    public IAsyncEnumerable<ICommand> ReactAsync(TestSagaState state, TestEvent @event, CancellationToken cancellationToken = default) =>
        AsyncEnumerable.Empty<ICommand>();
}
