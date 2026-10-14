using System.Text;
using FluentAssertions;

namespace Aggregates.KurrentDB.Migration;

/// <summary>
/// Reads the 0.x event-type filters out of a <c>$PersistentConfig</c> event, the way the
/// server wrote it during the spike (v1-07).
/// </summary>
public class ZeroXGroupConfigTests {
    const string Config = """
        {
          "version": "2",
          "entries": [
            {
              "stream": "$all",
              "group": "A@v1",
              "filter": {
                "context": "eventtype",
                "type": "regex",
                "data": "^(?:IntegrationTests\\.OrderPlaced@v1)$",
                "isAllStream": true
              },
              "messageTimeout": 30000,
              "startFrom": -9223372036854775808,
              "startPosition": "C:0/P:0",
              "liveBufferSize": 500,
              "historyBufferSize": 500,
              "maxRetryCount": 10,
              "readBatchSize": 20,
              "checkPointAfter": 2000,
              "minCheckPointCount": 10,
              "maxCheckPointCount": 1000,
              "namedConsumerStrategy": "RoundRobin"
            },
            {
              "stream": "$all",
              "group": "B@v1",
              "filter": {
                "context": "eventtype",
                "type": "regex",
                "data": "^(?:IntegrationTests\\.OrderShipped@v1)$",
                "isAllStream": true
              }
            },
            {
              "stream": "orders",
              "group": "C@v1",
              "filter": {
                "context": "eventtype",
                "type": "regex",
                "data": "^(?:IntegrationTests\\.OrderPlaced@v1)$",
                "isAllStream": false
              }
            },
            {
              "stream": "$all",
              "group": "D@v1",
              "filter": {
                "context": "eventtype",
                "type": "prefix",
                "data": "IntegrationTests",
                "isAllStream": true
              }
            }
          ]
        }
        """;

    public class ParseFilters {
        [Fact]
        public void ReturnsOnlyTheAllStreamRegexFilters() {
            var filters = ZeroXGroupConfig.ParseFilters(Encoding.UTF8.GetBytes(Config));
            filters.Should().HaveCount(2);
            filters["A@v1"].Should().Be("^(?:IntegrationTests\\.OrderPlaced@v1)$");
            filters["B@v1"].Should().Be("^(?:IntegrationTests\\.OrderShipped@v1)$");
            filters.Should().NotContainKey("C@v1", "a group on a stream is not a 0.x group");
            filters.Should().NotContainKey("D@v1", "0.x only created event-type regex filters");
        }

        [Fact]
        public void InvalidJson_YieldsNoFilters() {
            ZeroXGroupConfig.ParseFilters(Encoding.UTF8.GetBytes("not json")).Should().BeEmpty();
        }

        [Fact]
        public void MissingEntries_YieldNoFilters() {
            ZeroXGroupConfig.ParseFilters(Encoding.UTF8.GetBytes("{}")).Should().BeEmpty();
        }
    }

    public class ParseFilterNames {
        [Fact]
        public void SplitsTheFilterIntoUnescapedNames() {
            ZeroXGroupConfig.ParseFilterNames("^(?:IntegrationTests\\.OrderPlaced@v1|A\\.B@v2)$")
                .Should().Equal("IntegrationTests.OrderPlaced@v1", "A.B@v2");
        }

        [Fact]
        public void ASingleName_YieldsOneName() {
            ZeroXGroupConfig.ParseFilterNames("^(?:IntegrationTests\\.OrderPlaced@v1)$")
                .Should().Equal("IntegrationTests.OrderPlaced@v1");
        }

        [Theory]
        [InlineData("")]
        [InlineData("^(?:)$")]
        [InlineData("^IntegrationTests\\.OrderPlaced@v1$")]
        [InlineData("^(?:A|)$")]
        public void AnyOtherShape_IsNull(string filter) {
            ZeroXGroupConfig.ParseFilterNames(filter).Should().BeNull();
        }
    }
}
