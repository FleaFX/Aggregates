using System.Text.RegularExpressions;

namespace Aggregates.Subscriptions;

/// <summary>
/// What a subscription delivers, and from where.
/// </summary>
/// <param name="FromPosition">
/// The exclusive starting position, or <see langword="null"/> to start from the beginning
/// (unless <paramref name="StartFromEnd"/> overrides this).
/// </param>
/// <param name="StartFromEnd">
/// When <see langword="true"/> and <paramref name="FromPosition"/> is <see langword="null"/>,
/// starts from the current end of the stream rather than the beginning.
/// </param>
/// <param name="EventTypes">
/// The stored type names the subscription delivers, or <see langword="null"/> to deliver
/// every event type except system events.
/// </param>
/// <param name="ProgressInterval">
/// Number of events the store passes without delivering them, because of the subscription's
/// event-type filter, after which it reports progress (see <see cref="SubscriptionMessage.IsProgress"/>).
/// </param>
public sealed record SubscriptionRequest(
    ulong? FromPosition,
    bool StartFromEnd,
    IReadOnlyList<string>? EventTypes = null,
    int ProgressInterval = SubscriptionCheckpointOptions.DefaultMaxFilteredEvents
) {

    /// <summary>
    /// <c>"^(?:a|b|c)$"</c> with every name escaped, or <see langword="null"/> when
    /// <see cref="EventTypes"/> is <see langword="null"/>.
    /// </summary>
    public string? EventTypePattern =>
        EventTypes is null ? null : $"^(?:{string.Join('|', EventTypes.Select(Regex.Escape))})$";
}
