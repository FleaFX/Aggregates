using Microsoft.Extensions.DependencyInjection;

namespace Aggregates.Sagas.MSSP;

/// <summary>
/// A builder for configuring the <c>Aggregates.Sagas.MSSP</c> package.
/// </summary>
public interface ISagasMsspBuilder {
    /// <summary>
    /// The underlying service collection.
    /// </summary>
    IServiceCollection Services { get; }
}
