using FluentAssertions;
using Grpc.Core;
using KurrentDB.Client;

namespace Aggregates.KurrentDB;

public class KurrentDbSubscriptionFactoryTests {
    public class IsTransient : KurrentDbSubscriptionFactoryTests, IAsyncDisposable {
        // The client connects lazily, so no server is needed to classify exceptions.
        readonly KurrentDBClient _client = new(KurrentDBClientSettings.Create("kurrentdb://localhost:2113?tls=false"));

        KurrentDbSubscriptionFactory Factory => new(_client, new KurrentDbOptions());

        public static TheoryData<Exception> NotTransient => [
            new AccessDeniedException(),
            new NotAuthenticatedException("not authenticated"),
            new RpcException(new Status(StatusCode.PermissionDenied, "")),
            new RpcException(new Status(StatusCode.Unauthenticated, "")),
            new RpcException(new Status(StatusCode.InvalidArgument, "")),
            new RpcException(new Status(StatusCode.Unimplemented, "")),
            new RpcException(new Status(StatusCode.FailedPrecondition, "")),
        ];

        public static TheoryData<Exception> Transient => [
            new RpcException(new Status(StatusCode.Unavailable, "")),
            new RpcException(new Status(StatusCode.DeadlineExceeded, "")),
            new RpcException(new Status(StatusCode.Internal, "")),
            new RpcException(new Status(StatusCode.Cancelled, "")),
            new NotLeaderException("localhost", 2113),
            new DiscoveryException(10),
            new IOException(),
            new HttpRequestException(),
            new ObjectDisposedException(nameof(KurrentDBClient)),
            new TimeoutException(),
            new InvalidOperationException(),
        ];

        [Theory, MemberData(nameof(NotTransient))]
        public void GivenConfigurationFailure_ReturnsFalse(Exception exception) {
            Factory.IsTransient(exception).Should().BeFalse();
        }

        [Theory, MemberData(nameof(Transient))]
        public void GivenOtherFailure_ReturnsTrue(Exception exception) {
            Factory.IsTransient(exception).Should().BeTrue();
        }

        public ValueTask DisposeAsync() => _client.DisposeAsync();
    }
}
