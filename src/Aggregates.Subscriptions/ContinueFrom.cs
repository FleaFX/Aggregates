namespace Aggregates.Subscriptions;

/// <summary>
/// Values for the continueFrom parameter of the projection, policy and saga contracts.
/// </summary>
public static class ContinueFrom {
    /// <summary>
    /// No predecessor: the subscription starts from the beginning (or the end with
    /// startFromEnd) instead of continuing from an older version's checkpoint.
    /// </summary>
    public const string None = "";
}
