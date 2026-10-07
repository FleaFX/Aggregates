namespace Aggregates;

record struct NoopCommand : ICommand;

record struct ScanTestEvent;

record ScanTestState : IState<ScanTestState, ScanTestEvent> {
    public static ScanTestState Initial => new();
    public ScanTestState Apply(ScanTestEvent @event) => this;
}

record ScanTestCommand(AggregateIdentifier Id) : ICommand<ScanTestState, ScanTestEvent> {
    public IAsyncEnumerable<ScanTestEvent> ProgressAsync(ScanTestState state, CancellationToken cancellationToken = default) =>
        AsyncEnumerable.Empty<ScanTestEvent>();
}

record OtherScanTestCommand(AggregateIdentifier Id) : ICommand<ScanTestState, ScanTestEvent> {
    public IAsyncEnumerable<ScanTestEvent> ProgressAsync(ScanTestState state, CancellationToken cancellationToken = default) =>
        AsyncEnumerable.Empty<ScanTestEvent>();
}
