using System.Collections.Immutable;
using System.Runtime.CompilerServices;

namespace Aggregates.Testing;

// The bill domain of the deferred command integration tests: a bill is planned with a due date,
// can be paid, and gets reminders that it refuses when they no longer apply.

interface IBillEvent {
    string BillId { get; }
}

[EventContract("BillPlanned", @namespace: "IntegrationTests")]
sealed record BillPlanned(string BillId, DateTimeOffset Due) : IBillEvent;

[EventContract("BillPaid", @namespace: "IntegrationTests")]
sealed record BillPaid(string BillId) : IBillEvent;

[EventContract("BillReminded", @namespace: "IntegrationTests")]
sealed record BillReminded(string BillId, DateTimeOffset Due, string Kind) : IBillEvent;

sealed record BillState(DateTimeOffset? Due, bool Paid, ImmutableHashSet<string> Reminded) : IState<BillState, IBillEvent> {
    public static BillState Initial => new(null, false, []);
    public BillState Apply(IBillEvent @event) => @event switch {
        BillPlanned planned => this with { Due = planned.Due },
        BillPaid => this with { Paid = true },
        BillReminded reminded => this with { Reminded = Reminded.Add($"{reminded.Due:O}/{reminded.Kind}") },
        _ => this
    };
}

sealed record PlanBill(AggregateIdentifier Id, DateTimeOffset Due) : ICommand<BillState, IBillEvent> {
    public async IAsyncEnumerable<IBillEvent> ProgressAsync(BillState state, [EnumeratorCancellation] CancellationToken cancellationToken = default) {
        yield return new BillPlanned(Id.Value, Due);
        await Task.CompletedTask;
    }
}

sealed record PayBill(AggregateIdentifier Id) : ICommand<BillState, IBillEvent> {
    public async IAsyncEnumerable<IBillEvent> ProgressAsync(BillState state, [EnumeratorCancellation] CancellationToken cancellationToken = default) {
        if (!state.Paid)
            yield return new BillPaid(Id.Value);
        await Task.CompletedTask;
    }
}

// Deferred: it decides itself whether the reminder still applies.
[EventContract("RemindBill", @namespace: "IntegrationTests")]
sealed record RemindBill(AggregateIdentifier Id, DateTimeOffset Due, string Kind) : ICommand<BillState, IBillEvent> {
    public async IAsyncEnumerable<IBillEvent> ProgressAsync(BillState state, [EnumeratorCancellation] CancellationToken cancellationToken = default) {
        if (!state.Paid && state.Due == Due && !state.Reminded.Contains($"{Due:O}/{Kind}"))
            yield return new BillReminded(Id.Value, Due, Kind);
        await Task.CompletedTask;
    }
}

[EventContract("FailBill", @namespace: "IntegrationTests")]
sealed record FailBill(AggregateIdentifier Id) : ICommand<BillState, IBillEvent> {
    public IAsyncEnumerable<IBillEvent> ProgressAsync(BillState state, CancellationToken cancellationToken = default) =>
        throw new InvalidOperationException("Failing on purpose.");
}
