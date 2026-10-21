using FluentAssertions;

namespace Aggregates;

public class EventMetadataTests {
    public class Constructor {
        [Fact]
        public void IsPublic() {
            var constructor = typeof(EventMetadata).GetConstructor([typeof(IEnumerable<KeyValuePair<string, object?>>)]);

            constructor.Should().NotBeNull();
            constructor!.IsPublic.Should().BeTrue("the application must be able to construct EventMetadata");
        }

        [Fact]
        public void GivenPairs_ContainsThem() {
            IEnumerable<KeyValuePair<string, object?>> entries = [
                KeyValuePair.Create<string, object?>("a", 1L),
                KeyValuePair.Create<string, object?>("b", "x")
            ];

            var metadata = new EventMetadata(entries);

            metadata.Count.Should().Be(2);
            metadata["a"].Should().Be(1L);
            metadata["b"].Should().Be("x");
        }

        [Fact]
        public void GivenDictionary_ContainsItsEntries() {
            var entries = new Dictionary<string, object?> { ["key"] = "value", ["none"] = null };

            var metadata = new EventMetadata(entries);

            metadata.Count.Should().Be(2);
            metadata["key"].Should().Be("value");
            metadata.ContainsKey("none").Should().BeTrue();
            metadata["none"].Should().BeNull();
        }

        [Fact]
        public void GivenEmptySequence_IsEmpty() {
            var metadata = new EventMetadata([]);

            metadata.Should().BeEmpty();
        }

        [Fact]
        public void GivenDuplicateKey_KeepsTheLastValue() {
            IEnumerable<KeyValuePair<string, object?>> entries = [
                KeyValuePair.Create<string, object?>("key", "first"),
                KeyValuePair.Create<string, object?>("other", 1L),
                KeyValuePair.Create<string, object?>("key", "last")
            ];

            var metadata = new EventMetadata(entries);

            metadata.Count.Should().Be(2);
            metadata["key"].Should().Be("last");
        }

        [Fact]
        public void GivenNullEntries_Throws() {
            IEnumerable<KeyValuePair<string, object?>> entries = null!;

            var act = () => new EventMetadata(entries);

            act.Should().Throw<ArgumentNullException>();
        }

        [Fact]
        public void GivenNullKey_Throws() {
            IEnumerable<KeyValuePair<string, object?>> entries = [
                KeyValuePair.Create<string, object?>(null!, "value")
            ];

            var act = () => new EventMetadata(entries);

            act.Should().Throw<ArgumentNullException>();
        }

        [Fact]
        public void GivenCaseInsensitiveDictionary_ComparesKeysOrdinally() {
            var entries = new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase) { ["Key"] = "value" };

            var metadata = new EventMetadata(entries);

            metadata.ContainsKey("Key").Should().BeTrue();
            metadata.ContainsKey("key").Should().BeFalse();
        }
    }
}
