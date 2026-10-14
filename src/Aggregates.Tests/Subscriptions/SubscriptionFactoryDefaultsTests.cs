using FakeItEasy;
using FluentAssertions;

namespace Aggregates.Subscriptions;

public class SubscriptionFactoryDefaultsTests {
    sealed class CustomFactory : ISubscriptionFactory {
        public ISubscription Subscribe(SubscriptionRequest request, CancellationToken cancellationToken = default) =>
            A.Fake<ISubscription>();
    }

    [Fact]
    public void IsTransient_DefaultsToTrue() {
        ISubscriptionFactory factory = new CustomFactory();

        factory.IsTransient(new UnauthorizedAccessException()).Should().BeTrue();
    }

    [Fact]
    public async Task GetEndPositionAsync_DefaultsToNull() {
        ISubscriptionFactory factory = new CustomFactory();

        (await factory.GetEndPositionAsync(TestContext.Current.CancellationToken)).Should().BeNull();
    }
}
