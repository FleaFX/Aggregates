namespace Aggregates.Analyzers;

/// <summary>
/// Metadata names of the Aggregates types the analyzers recognise. The analyzers don't reference the
/// Aggregates assemblies; a test checks these names against the real types.
/// </summary>
static class KnownTypes {
    /// <summary>
    /// <c>Aggregates.EventContractAttribute</c>.
    /// </summary>
    internal const string EventContractAttribute = "Aggregates.EventContractAttribute";

    /// <summary>
    /// <c>Aggregates.ICommand&lt;TState, TEvent&gt;</c>.
    /// </summary>
    internal const string Command = "Aggregates.ICommand`2";

    /// <summary>
    /// <c>Aggregates.Sagas.ISaga&lt;TSagaState, TEvent&gt;</c>; <c>ISaga&lt;TSagaState, TEvent, TDeadline&gt;</c> derives from it.
    /// </summary>
    internal const string Saga = "Aggregates.Sagas.ISaga`2";

    /// <summary>
    /// <c>Aggregates.Policies.IPolicy&lt;TEvent&gt;</c>.
    /// </summary>
    internal const string Policy = "Aggregates.Policies.IPolicy`1";

    /// <summary>
    /// <c>Aggregates.Projections.IProjection&lt;TEvent&gt;</c>.
    /// </summary>
    internal const string Projection = "Aggregates.Projections.IProjection`1";
}
