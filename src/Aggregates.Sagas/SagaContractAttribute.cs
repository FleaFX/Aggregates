using Aggregates.Subscriptions;

namespace Aggregates.Sagas;

/// <summary>
/// Configures the identity and behaviour of a saga.
/// </summary>
/// <param name="name">The name of the saga.</param>
/// <param name="version">The version of the saga. Defaults to 1.</param>
/// <param name="namespace">An optional namespace prepended to the saga name.</param>
/// <param name="continueFrom">
/// The subscription id of a predecessor contract whose checkpoint this saga continues from
/// when it has none of its own. When omitted, every previous version from
/// <paramref name="version"/> - 1 down to 1 is a predecessor, nearest first. An empty string
/// (<see cref="Aggregates.Subscriptions.ContinueFrom.None"/>) means no predecessor: the
/// subscription starts from the beginning, or from the end with
/// <paramref name="startFromEnd"/>.
/// </param>
/// <param name="startFromEnd">
/// When <see langword="true"/>, the saga starts processing from the end of the stream
/// rather than the beginning.
/// </param>
/// <param name="onError">
/// What the subscription does with an event the saga still fails on after all retries:
/// <see cref="SubscriptionErrorMode.Park"/> (the default) parks it and continues;
/// <see cref="SubscriptionErrorMode.Block"/> retries it until it succeeds.
/// It applies to events only; deferred commands and deadlines are always parked
/// (see <see cref="SubscriptionContractAttribute.OnError"/>).
/// </param>
[AttributeUsage(AttributeTargets.Class | AttributeTargets.Struct, Inherited = false)]
public sealed class SagaContractAttribute(
    string name,
    int version = 1,
    string? @namespace = null,
    string? continueFrom = null,
    bool startFromEnd = false,
    SubscriptionErrorMode onError = SubscriptionErrorMode.Park
) : SubscriptionContractAttribute(name, version, @namespace, continueFrom, startFromEnd, onError);
