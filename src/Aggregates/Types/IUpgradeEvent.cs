namespace Aggregates;

/// <summary>
/// Implementers provide the ability to upgrade a <typeparamref name="TOldVersion"/> to a <typeparamref name="TNewVersion"/>.
/// </summary>
/// <typeparam name="TOldVersion">The type of the old version of the event.</typeparam>
/// <typeparam name="TNewVersion">The type of the new version of the event.</typeparam>
public interface IUpgradeEvent<in TOldVersion, out TNewVersion> {
    /// <summary>
    /// Upgrades the given <paramref name="event"/> to a <typeparamref name="TNewVersion"/>.
    /// </summary>
    /// <param name="event">The <typeparamref name="TOldVersion"/> to upgrade.</param>
    /// <returns>A <typeparamref name="TNewVersion"/>.</returns>
    TNewVersion Upgrade(TOldVersion @event);
}