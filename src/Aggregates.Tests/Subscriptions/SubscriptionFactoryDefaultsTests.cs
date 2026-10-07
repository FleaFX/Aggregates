using FakeItEasy;
using FluentAssertions;

namespace Aggregates.Subscriptions;

public class SubscriptionFactoryDefaultsTests {
    sealed class CustomFactory : ISubscriptionFactory {
        public ISubscription Subscribe(ulong? fromPosition, bool startFromEnd, CancellationToken cancellationToken = default) =>
            A.Fake<ISubscription>();
    }

    [Fact]
    public void IsTransient_DefaultsToTrue() {
        ISubscriptionFactory factory = new CustomFactory();

        factory.IsTransient(new UnauthorizedAccessException()).Should().BeTrue();
    }
}
