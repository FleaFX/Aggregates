using FakeItEasy;
using FluentAssertions;

namespace Aggregates;

public class MetadataAwareCommandHandlerTests {
    [Fact]
    public async Task GivenCommand_ScopeIsActiveInsideInnerHandler() {
        MetadataScope? captured = null;
        var inner = A.Fake<CommandHandler<NoopCommand>>();
        A.CallTo(() => inner.HandleAsync(A<NoopCommand>._, A<CancellationToken>._))
            .Invokes(() => captured = MetadataScope.Current)
            .Returns(ValueTask.CompletedTask);
        var handler = BuildHandler(inner);

        await handler.HandleAsync(new NoopCommand(), TestContext.Current.CancellationToken);

        captured.Should().NotBeNull();
    }

    [Fact]
    public async Task GivenCommand_ScopeIsNoLongerActiveAfterHandling() {
        var inner = A.Fake<CommandHandler<NoopCommand>>();
        A.CallTo(() => inner.HandleAsync(A<NoopCommand>._, A<CancellationToken>._))
            .Returns(ValueTask.CompletedTask);
        var handler = BuildHandler(inner);

        await handler.HandleAsync(new NoopCommand(), TestContext.Current.CancellationToken);

        MetadataScope.Current.Should().BeNull();
    }

    [Fact]
    public async Task GivenNoParentScope_StartsWithEmptyScope() {
        MetadataScope? captured = null;
        var inner = A.Fake<CommandHandler<NoopCommand>>();
        A.CallTo(() => inner.HandleAsync(A<NoopCommand>._, A<CancellationToken>._))
            .Invokes(() => captured = MetadataScope.Current)
            .Returns(ValueTask.CompletedTask);
        var handler = BuildHandler(inner);

        await handler.HandleAsync(new NoopCommand(), TestContext.Current.CancellationToken);

        captured!.Snapshot().Should().BeEmpty();
    }

    [Fact]
    public async Task GivenParentScope_NewScopeIsSeededFromParent() {
        await using var parentScope = new MetadataScope();
        parentScope.Add("correlationId", "abc-123");

        MetadataScope? captured = null;
        var inner = A.Fake<CommandHandler<NoopCommand>>();
        A.CallTo(() => inner.HandleAsync(A<NoopCommand>._, A<CancellationToken>._))
            .Invokes(() => captured = MetadataScope.Current)
            .Returns(ValueTask.CompletedTask);
        var handler = BuildHandler(inner);

        await handler.HandleAsync(new NoopCommand(), TestContext.Current.CancellationToken);

        captured!.Snapshot()["correlationId"].Should().Be("abc-123");
    }

    [Fact]
    public async Task GivenParentScope_InnerScopeIsNotSameInstanceAsParent() {
        await using var parentScope = new MetadataScope();

        MetadataScope? captured = null;
        var inner = A.Fake<CommandHandler<NoopCommand>>();
        A.CallTo(() => inner.HandleAsync(A<NoopCommand>._, A<CancellationToken>._))
            .Invokes(() => captured = MetadataScope.Current)
            .Returns(ValueTask.CompletedTask);
        var handler = BuildHandler(inner);

        await handler.HandleAsync(new NoopCommand(), TestContext.Current.CancellationToken);

        captured.Should().NotBeSameAs(parentScope);
    }

    [Fact]
    public async Task GivenConcurrencyRetry_EachAttemptGetsAFreshScope() {
        // Each retry must start with a fresh scope so accumulated metadata from a failed
        // attempt does not carry over. The snapshot seeds the new scope from the parent,
        // which is identical for every attempt (the parent scope is outside RetryCommandHandler).
        var scopes = new List<MetadataScope?>();
        var inner = A.Fake<CommandHandler<NoopCommand>>();
        A.CallTo(() => inner.HandleAsync(A<NoopCommand>._, A<CancellationToken>._))
            .Invokes(() => scopes.Add(MetadataScope.Current))
            .Throws(new ConcurrencyException("id", AggregateVersion.None, AggregateVersion.None)).Twice()
            .Then.Invokes(() => scopes.Add(MetadataScope.Current))
            .Returns(ValueTask.CompletedTask);

        var uow = new UnitOfWorkAwareCommandHandler<NoopCommand>(inner, _ => ValueTask.CompletedTask);
        var metadata = new MetadataAwareCommandHandler<NoopCommand>(uow);
        var retry = new RetryCommandHandler<NoopCommand>(metadata, maxAttempts: 3);

        await retry.HandleAsync(new NoopCommand(), TestContext.Current.CancellationToken);

        scopes.Should().HaveCount(3);
        // Each attempt should have produced a distinct scope instance.
        scopes.Distinct().Should().HaveCount(3);
    }

    static MetadataAwareCommandHandler<NoopCommand> BuildHandler(CommandHandler<NoopCommand> inner) =>
        new(new UnitOfWorkAwareCommandHandler<NoopCommand>(inner, _ => ValueTask.CompletedTask));
}
