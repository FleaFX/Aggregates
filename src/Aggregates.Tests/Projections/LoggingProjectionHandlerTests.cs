using FakeItEasy;
using FluentAssertions;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Testing;

namespace Aggregates.Projections;

public class LoggingProjectionHandlerTests {

    public class HandleAsync {
        readonly IProjection<ProjectionTestEvent> _projection = A.Fake<IProjection<ProjectionTestEvent>>();
        readonly FakeLogger<LoggingProjectionHandler<IProjection<ProjectionTestEvent>, ProjectionTestEvent>> _logger = new();

        // The interface itself as TProjection, so the projection can be faked.
        LoggingProjectionHandler<IProjection<ProjectionTestEvent>, ProjectionTestEvent> BuildHandler() =>
            new(new ProjectionHandler<IProjection<ProjectionTestEvent>, ProjectionTestEvent>(_projection), _logger);

        void ProjectionThrows() =>
            A.CallTo(() => _projection.ProjectAsync(A<ProjectionTestEvent>._, A<EventMetadata>._, A<CancellationToken>._))
                .Throws<InvalidOperationException>();

        [Fact]
        public async Task GivenSuccess_LogsProjectingAndProjected() {
            await BuildHandler().HandleAsync(new ProjectionTestEvent(1), EventMetadata.Empty, TestContext.Current.CancellationToken);

            _logger.Collector.GetSnapshot()
                .Should().Contain(r => r.Level == LogLevel.Debug && r.Message.Contains("Projecting"))
                .And.Contain(r => r.Level == LogLevel.Debug && r.Message.Contains("Projected"));
        }

        [Fact]
        public async Task LogsTheEventTypeAndTheProjection() {
            await BuildHandler().HandleAsync(new ProjectionTestEvent(1), EventMetadata.Empty, TestContext.Current.CancellationToken);

            _logger.Collector.GetSnapshot()
                .Should().Contain(r => r.Message == "Projecting ProjectionTestEvent in IProjection`1");
        }

        [Fact]
        public async Task GivenSuccess_DoesNotLogError() {
            await BuildHandler().HandleAsync(new ProjectionTestEvent(1), EventMetadata.Empty, TestContext.Current.CancellationToken);

            _logger.Collector.GetSnapshot()
                .Should().NotContain(r => r.Level == LogLevel.Error);
        }

        [Fact]
        public async Task GivenException_LogsError() {
            ProjectionThrows();

            var act = () => BuildHandler().HandleAsync(
                new ProjectionTestEvent(1),
                EventMetadata.Empty,
                TestContext.Current.CancellationToken).AsTask();

            await act.Should().ThrowAsync<InvalidOperationException>();
            _logger.Collector.GetSnapshot()
                .Should().Contain(r => r.Level == LogLevel.Error);
        }

        [Fact]
        public async Task GivenException_Rethrows() {
            ProjectionThrows();

            var act = () => BuildHandler().HandleAsync(
                new ProjectionTestEvent(1),
                EventMetadata.Empty,
                TestContext.Current.CancellationToken).AsTask();

            await act.Should().ThrowAsync<InvalidOperationException>();
        }

        [Fact]
        public async Task DelegatesEventAndMetadataToTheProjection() {
            var @event = new ProjectionTestEvent(99);

            await BuildHandler().HandleAsync(@event, EventMetadata.Empty, TestContext.Current.CancellationToken);

            A.CallTo(() => _projection.ProjectAsync(@event, EventMetadata.Empty, A<CancellationToken>._))
                .MustHaveHappenedOnceExactly();
        }
    }
}
