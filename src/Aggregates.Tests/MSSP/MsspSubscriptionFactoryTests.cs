using System.Threading.Channels;
using FakeItEasy;
using FluentAssertions;
using Grpc.Core;
using IMsspClient = global::MSSP.IMsspClient;

namespace Aggregates.MSSP;

public class MsspSubscriptionFactoryTests {
    public class IsTransient : MsspSubscriptionFactoryTests {
        readonly MsspSubscriptionFactory _factory = new(A.Fake<IMsspClient>(), new MsspOptions());

        public static TheoryData<Exception> NotTransient => [
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
            new TimeoutException("Could not determine the cluster leader within the timeout period."),
            new ObjectDisposedException("StoreEngine"),
            new ChannelClosedException(),
            new IOException(),
            new InvalidOperationException(),
        ];

        [Theory, MemberData(nameof(NotTransient))]
        public void GivenConfigurationFailure_ReturnsFalse(Exception exception) {
            _factory.IsTransient(exception).Should().BeFalse();
        }

        [Theory, MemberData(nameof(Transient))]
        public void GivenOtherFailure_ReturnsTrue(Exception exception) {
            _factory.IsTransient(exception).Should().BeTrue();
        }
    }
}
