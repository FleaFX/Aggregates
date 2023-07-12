namespace Aggregates.Types;

interface IUpgradeEvent<in TOldVersion, out TNewVersion> {
    /// <summary>
    /// Upgrades the given <paramref name="event"/> to a <typeparamref name="TNewVersion"/>.
    /// </summary>
    /// <param name="event">The <typeparamref name="TOldVersion"/> to upgrade.</param>
    /// <returns>A <typeparamref name="TNewVersion"/>.</returns>
    TNewVersion Upgrade(TOldVersion @event);
}