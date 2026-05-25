using FakeItEasy;
using FluentAssertions;

namespace Aggregates.Projections;

public class CommitTests {

    public class Create {
        [Fact]
        public async Task CommitAsync_CompletesWithoutError() {
            // An empty commit chain should be a no-op.
            var act = () => Commit.Create().CommitAsync(TestContext.Current.CancellationToken).AsTask();

            await act.Should().NotThrowAsync();
        }
    }

    public class Use {
        [Fact]
        public async Task CommitAsync_CallsChildCommitAsync() {
            var child = A.Fake<ICommit>();
            var commit = Commit.Create().Use(() => child);

            await commit.CommitAsync(TestContext.Current.CancellationToken);

            A.CallTo(() => child.CommitAsync(A<CancellationToken>._))
                .MustHaveHappenedOnceExactly();
        }

        [Fact]
        public void InvokesApplicatorEagerly() {
            var callCount = 0;
            var child = A.Fake<ICommit>();

            _ = Commit.Create().Use(() => { callCount++; return child; });

            callCount.Should().Be(1, because: "Use() should invoke the applicator immediately");
        }

        [Fact]
        public async Task MultipleChained_ExecutesChildrenInOrder() {
            var order = new List<int>();
            var child1 = A.Fake<ICommit>();
            var child2 = A.Fake<ICommit>();
            A.CallTo(() => child1.CommitAsync(A<CancellationToken>._))
                .Invokes(() => order.Add(1))
                .Returns(ValueTask.CompletedTask);
            A.CallTo(() => child2.CommitAsync(A<CancellationToken>._))
                .Invokes(() => order.Add(2))
                .Returns(ValueTask.CompletedTask);

            var commit = Commit.Create().Use(() => child1).Use(() => child2);
            await commit.CommitAsync(TestContext.Current.CancellationToken);

            order.Should().Equal(new[] { 1, 2 }, because: "commits must execute in the order they were appended");
        }

        [Fact]
        public async Task MultipleChained_ExecutesAllChildren() {
            var child1 = A.Fake<ICommit>();
            var child2 = A.Fake<ICommit>();
            var child3 = A.Fake<ICommit>();

            var commit = Commit.Create().Use(() => child1).Use(() => child2).Use(() => child3);
            await commit.CommitAsync(TestContext.Current.CancellationToken);

            A.CallTo(() => child1.CommitAsync(A<CancellationToken>._)).MustHaveHappenedOnceExactly();
            A.CallTo(() => child2.CommitAsync(A<CancellationToken>._)).MustHaveHappenedOnceExactly();
            A.CallTo(() => child3.CommitAsync(A<CancellationToken>._)).MustHaveHappenedOnceExactly();
        }
    }

    public class UseDeferred {
        [Fact]
        public async Task FactoryNotCalledBeforeCommitAsync() {
            var factoryCalled = false;
            var child = A.Fake<ICommit>();

            var commit = Commit.Create().Use<ICommit>(ct => {
                factoryCalled = true;
                return ValueTask.FromResult(child);
            });

            factoryCalled.Should().BeFalse("factory must not be called before CommitAsync");

            await commit.CommitAsync(TestContext.Current.CancellationToken);

            factoryCalled.Should().BeTrue("factory must be called during CommitAsync");
        }

        [Fact]
        public async Task CommitAsync_CallsChildCommitAsync() {
            var child = A.Fake<ICommit>();
            var commit = Commit.Create().Use<ICommit>(ct => ValueTask.FromResult(child));

            await commit.CommitAsync(TestContext.Current.CancellationToken);

            A.CallTo(() => child.CommitAsync(A<CancellationToken>._))
                .MustHaveHappenedOnceExactly();
        }
    }
}
