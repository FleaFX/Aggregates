namespace Aggregates.Projections;

record struct ProjectionTestEvent(int Value);

// Minimal concrete IProjection implementation used only as a type parameter marker in
// ProjectionHandler<TProjection, TEvent> — actual projection logic is always faked.
class TestProjection : IProjection<ProjectionTestEvent> {
    public ValueTask<ICommit> ProjectAsync(
        ProjectionTestEvent @event,
        EventMetadata metadata,
        CancellationToken cancellationToken = default) =>
        ValueTask.FromResult(Commit.Create());
}
