using FakeItEasy;
using FluentAssertions;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Testing;

namespace Aggregates.Projections;

public class LoggingProjectionHandlerTests {

    public class HandleAsync {
        static LoggingProjectionHandler<ProjectionTestEvent> BuildHandler(
            ProjectionHandler<ProjectionTestEvent> inner,
            FakeLogger<LoggingProjectionHandler<ProjectionTestEvent>> logger) =>
            new(inner, logger);

        [Fact]
        public async Task GivenSuccess_LogsProjectingAndProjected() {
            var logger = new FakeLogger<LoggingProjectionHandler<ProjectionTestEvent>>();
            var handler = BuildHandler(A.Fake<ProjectionHandler<ProjectionTestEvent>>(), logger);

            await handler.HandleAsync(new ProjectionTestEvent(1), EventMetadata.Empty, TestContext.Current.CancellationToken);

            logger.Collector.GetSnapshot()
                .Should().Contain(r => r.Level == LogLevel.Debug && r.Message.Contains("Projecting"))
                .And.Contain(r => r.Level == LogLevel.Debug && r.Message.Contains("Projected"));
        }

        [Fact]
        public async Task GivenSuccess_DoesNotLogError() {
            var logger = new FakeLogger<LoggingProjectionHandler<ProjectionTestEvent>>();
            var handler = BuildHandler(A.Fake<ProjectionHandler<ProjectionTestEvent>>(), logger);

            await handler.HandleAsync(new ProjectionTestEvent(1), EventMetadata.Empty, TestContext.Current.CancellationToken);

            logger.Collector.GetSnapshot()
                .Should().NotContain(r => r.Level == LogLevel.Error);
        }

        [Fact]
        public async Task GivenException_LogsError() {
            var logger = new FakeLogger<LoggingProjectionHandler<ProjectionTestEvent>>();
            var inner = A.Fake<ProjectionHandler<ProjectionTestEvent>>();
            A.CallTo(() => inner.HandleAsync(A<ProjectionTestEvent>._, A<EventMetadata>._, A<CancellationToken>._))
                .Throws<InvalidOperationException>();
            var handler = BuildHandler(inner, logger);

            var act = () => handler.HandleAsync(
                new ProjectionTestEvent(1),
                EventMetadata.Empty,
                TestContext.Current.CancellationToken).AsTask();

            await act.Should().ThrowAsync<InvalidOperationException>();
            logger.Collector.GetSnapshot()
                .Should().Contain(r => r.Level == LogLevel.Error);
        }

        [Fact]
        public async Task GivenException_Rethrows() {
            var logger = new FakeLogger<LoggingProjectionHandler<ProjectionTestEvent>>();
            var inner = A.Fake<ProjectionHandler<ProjectionTestEvent>>();
            A.CallTo(() => inner.HandleAsync(A<ProjectionTestEvent>._, A<EventMetadata>._, A<CancellationToken>._))
                .Throws<InvalidOperationException>();
            var handler = BuildHandler(inner, logger);

            var act = () => handler.HandleAsync(
                new ProjectionTestEvent(1),
                EventMetadata.Empty,
                TestContext.Current.CancellationToken).AsTask();

            await act.Should().ThrowAsync<InvalidOperationException>();
        }

        [Fact]
        public async Task DelegatesEventAndMetadataToInnerHandler() {
            var logger = new FakeLogger<LoggingProjectionHandler<ProjectionTestEvent>>();
            var inner = A.Fake<ProjectionHandler<ProjectionTestEvent>>();
            var handler = BuildHandler(inner, logger);
            var @event = new ProjectionTestEvent(99);

            await handler.HandleAsync(@event, EventMetadata.Empty, TestContext.Current.CancellationToken);

            A.CallTo(() => inner.HandleAsync(@event, EventMetadata.Empty, A<CancellationToken>._))
                .MustHaveHappenedOnceExactly();
        }
    }
}
