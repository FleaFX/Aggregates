namespace Aggregates.Testing;

/// <summary>
/// Creates a fresh store per test, for each <see cref="Transport"/>.
/// </summary>
public static class Stores {
    /// <summary>
    /// All transports, for <c>[Theory, MemberData(nameof(Stores.All), MemberType = typeof(Stores))]</c>.
    /// </summary>
    public static TheoryData<Transport> All => [Transport.KurrentDB, Transport.MSSP];

    /// <summary>
    /// Starts a fresh, empty store for <paramref name="transport"/>.
    /// </summary>
    internal static async Task<IStoreFixture> StartAsync(Transport transport) => transport switch {
        Transport.KurrentDB => await KurrentDbStoreFixture.StartAsync(),
        Transport.MSSP => await MsspStoreFixture.StartAsync(),
        _ => throw new ArgumentOutOfRangeException(nameof(transport), transport, null)
    };
}
