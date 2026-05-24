using FluentAssertions;

namespace Aggregates;

public class MetadataScopeTests {
    public class Current {
        [Fact]
        public void GivenNoActiveScope_IsNull() {
            MetadataScope.Current.Should().BeNull();
        }

        [Fact]
        public async Task GivenActiveScope_IsNotNull() {
            await using var scope = new MetadataScope();

            MetadataScope.Current.Should().NotBeNull();
        }

        [Fact]
        public async Task GivenDisposedScope_IsNull() {
            await using (new MetadataScope()) { }

            MetadataScope.Current.Should().BeNull();
        }

        [Fact]
        public async Task GivenNestedScopes_CurrentIsInnermostScope() {
            await using var outer = new MetadataScope();
            await using var inner = new MetadataScope();

            MetadataScope.Current.Should().BeSameAs(inner);
        }

        [Fact]
        public async Task GivenInnerScopeDisposed_CurrentReturnsOuterScope() {
            await using var outer = new MetadataScope();
            await using (new MetadataScope()) { }

            MetadataScope.Current.Should().BeSameAs(outer);
        }
    }

    public class Add {
        [Fact]
        public async Task GivenSingle_StoresValue() {
            await using var scope = new MetadataScope();

            scope.Add("key", "value");

            scope.Snapshot().TryGetValue("key", out var result).Should().BeTrue();
            result.Should().Be("value");
        }

        [Fact]
        public async Task GivenSingle_OverwritesExistingValue() {
            await using var scope = new MetadataScope();
            scope.Add("key", "first");

            scope.Add("key", "second");

            scope.Snapshot()["key"].Should().Be("second");
        }

        [Fact]
        public async Task GivenMultiple_AccumulatesValues() {
            await using var scope = new MetadataScope();

            scope.Add("key", "a", MetadataMultiplicity.Multiple);
            scope.Add("key", "b", MetadataMultiplicity.Multiple);
            scope.Add("key", "c", MetadataMultiplicity.Multiple);

            scope.Snapshot()["key"].Should().BeEquivalentTo(new object?[] { "a", "b", "c" });
        }

        [Fact]
        public async Task GivenMultiple_IgnoresDuplicateValues() {
            await using var scope = new MetadataScope();

            scope.Add("key", "a", MetadataMultiplicity.Multiple);
            scope.Add("key", "a", MetadataMultiplicity.Multiple);

            var result = (object?[])scope.Snapshot()["key"]!;
            result.Should().HaveCount(1);
        }

        [Fact]
        public async Task GivenMultiple_AfterExistingScalar_IncludesBothValues() {
            await using var scope = new MetadataScope();
            scope.Add("key", "scalar");

            scope.Add("key", "appended", MetadataMultiplicity.Multiple);

            scope.Snapshot()["key"].Should().BeEquivalentTo(new object?[] { "scalar", "appended" });
        }

        [Fact]
        public async Task GivenSingle_AfterExistingMultiple_OverwritesWithScalar() {
            await using var scope = new MetadataScope();
            scope.Add("key", "a", MetadataMultiplicity.Multiple);
            scope.Add("key", "b", MetadataMultiplicity.Multiple);

            scope.Add("key", "overwrite");

            scope.Snapshot()["key"].Should().Be("overwrite");
        }
    }

    public class Snapshot {
        [Fact]
        public async Task GivenEmptyScope_ReturnsEmptyMetadata() {
            await using var scope = new MetadataScope();

            scope.Snapshot().Should().BeEmpty();
        }

        [Fact]
        public async Task GivenSingleEntry_ReturnsScalarValue() {
            await using var scope = new MetadataScope();
            scope.Add("key", 42);

            scope.Snapshot()["key"].Should().Be(42);
        }

        [Fact]
        public async Task GivenMultipleEntry_ReturnsArray() {
            await using var scope = new MetadataScope();
            scope.Add("key", "a", MetadataMultiplicity.Multiple);
            scope.Add("key", "b", MetadataMultiplicity.Multiple);

            scope.Snapshot()["key"].Should().BeOfType<object?[]>();
        }

        [Fact]
        public async Task CalledTwice_ReturnsSeparateInstances() {
            await using var scope = new MetadataScope();
            scope.Add("key", "value");

            var first = scope.Snapshot();
            var second = scope.Snapshot();

            first.Should().NotBeSameAs(second);
        }
    }

    public class SeedConstructor {
        [Fact]
        public async Task GivenScalarInSeed_IsAccessibleInNewScope() {
            await using var outer = new MetadataScope();
            outer.Add("correlationId", "abc-123");
            var seed = outer.Snapshot();

            await using var inner = new MetadataScope(seed);

            inner.Snapshot()["correlationId"].Should().Be("abc-123");
        }

        [Fact]
        public async Task GivenArrayInSeed_FurtherMultipleAddsAreFlattenedCorrectly() {
            // This test guards against the array-in-array bug:
            // an array value from a snapshot must be restored as an accumulator,
            // not treated as a single scalar, so that further Multiple additions append correctly.
            await using var outer = new MetadataScope();
            outer.Add("tags", "a", MetadataMultiplicity.Multiple);
            outer.Add("tags", "b", MetadataMultiplicity.Multiple);
            var seed = outer.Snapshot(); // { "tags": ["a", "b"] }

            await using var inner = new MetadataScope(seed);
            inner.Add("tags", "c", MetadataMultiplicity.Multiple);

            inner.Snapshot()["tags"].Should().BeEquivalentTo(new object?[] { "a", "b", "c" });
        }

        [Fact]
        public async Task GivenArrayInSeed_AddingSameValueIsIgnored() {
            await using var outer = new MetadataScope();
            outer.Add("tags", "a", MetadataMultiplicity.Multiple);
            var seed = outer.Snapshot();

            await using var inner = new MetadataScope(seed);
            inner.Add("tags", "a", MetadataMultiplicity.Multiple);

            var result = (object?[])inner.Snapshot()["tags"]!;
            result.Should().HaveCount(1);
        }

        [Fact]
        public async Task GivenSeedWithScalar_OverwriteWithSingleWorks() {
            await using var outer = new MetadataScope();
            outer.Add("key", "original");
            var seed = outer.Snapshot();

            await using var inner = new MetadataScope(seed);
            inner.Add("key", "overwritten");

            inner.Snapshot()["key"].Should().Be("overwritten");
        }
    }
}
