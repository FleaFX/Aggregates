using FakeItEasy;
using FluentAssertions;

namespace Aggregates.Projections;

public class ProjectionHandlerTests {

    public class HandleAsync {
        static ProjectionHandler<TestProjection, ProjectionTestEvent> BuildHandler(
            IProjection<ProjectionTestEvent> projection) =>
            new(projection);

        [Fact]
        public async Task CallsProjectAsync_WithEvent() {
            var projection = A.Fake<IProjection<ProjectionTestEvent>>();
            var commit = A.Fake<ICommit>();
            A.CallTo(() => projection.ProjectAsync(A<ProjectionTestEvent>._, A<EventMetadata>._, A<CancellationToken>._))
                .Returns(ValueTask.FromResult(commit));
            var @event = new ProjectionTestEvent(42);

            await BuildHandler(projection).HandleAsync(@event, EventMetadata.Empty, TestContext.Current.CancellationToken);

            A.CallTo(() => projection.ProjectAsync(@event, A<EventMetadata>._, A<CancellationToken>._))
                .MustHaveHappenedOnceExactly();
        }

        [Fact]
        public async Task CallsProjectAsync_WithMetadata() {
            var projection = A.Fake<IProjection<ProjectionTestEvent>>();
            var commit = A.Fake<ICommit>();
            A.CallTo(() => projection.ProjectAsync(A<ProjectionTestEvent>._, A<EventMetadata>._, A<CancellationToken>._))
                .Returns(ValueTask.FromResult(commit));

            await BuildHandler(projection).HandleAsync(
                new ProjectionTestEvent(1),
                EventMetadata.Empty,
                TestContext.Current.CancellationToken);

            A.CallTo(() => projection.ProjectAsync(A<ProjectionTestEvent>._, EventMetadata.Empty, A<CancellationToken>._))
                .MustHaveHappenedOnceExactly();
        }

        [Fact]
        public async Task CommitsReturnedCommit() {
            var projection = A.Fake<IProjection<ProjectionTestEvent>>();
            var commit = A.Fake<ICommit>();
            A.CallTo(() => projection.ProjectAsync(A<ProjectionTestEvent>._, A<EventMetadata>._, A<CancellationToken>._))
                .Returns(ValueTask.FromResult(commit));

            await BuildHandler(projection).HandleAsync(
                new ProjectionTestEvent(1),
                EventMetadata.Empty,
                TestContext.Current.CancellationToken);

            A.CallTo(() => commit.CommitAsync(A<CancellationToken>._))
                .MustHaveHappenedOnceExactly();
        }

        [Fact]
        public async Task WhenProjectAsyncThrows_DoesNotCommit() {
            var projection = A.Fake<IProjection<ProjectionTestEvent>>();
            var commit = A.Fake<ICommit>();
            A.CallTo(() => projection.ProjectAsync(A<ProjectionTestEvent>._, A<EventMetadata>._, A<CancellationToken>._))
                .Throws<InvalidOperationException>();

            var act = () => BuildHandler(projection)
                .HandleAsync(new ProjectionTestEvent(1), EventMetadata.Empty, TestContext.Current.CancellationToken)
                .AsTask();

            await act.Should().ThrowAsync<InvalidOperationException>();
            A.CallTo(() => commit.CommitAsync(A<CancellationToken>._))
                .MustNotHaveHappened();
        }
    }
}
