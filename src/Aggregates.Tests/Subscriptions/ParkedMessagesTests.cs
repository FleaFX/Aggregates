using System.Text;
using Aggregates.Testing;
using FakeItEasy;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;

namespace Aggregates.Subscriptions;

/// <summary>
/// Tests <see cref="ParkedMessages"/> against a <see cref="FakeParkedMessageStore"/>: listing,
/// replaying and dismissing parked events, deferred commands and deadlines.
/// </summary>
public class ParkedMessagesTests {
    readonly FakeParkedMessageStore _store = new();
    readonly IReplayableSubscription _orders = A.Fake<IReplayableSubscription>();
    readonly IReplayableSubscription _reminders = A.Fake<IReplayableSubscription>();
    readonly SubscriptionRetryPolicy _retryPolicy = new(A.Fake<IParkedMessageSink>(),
        new SubscriptionErrorHandlingOptions { MaxRetries = 2, InitialDelay = TimeSpan.Zero, MaxDelay = TimeSpan.Zero });
    readonly IServiceScopeFactory _scopeFactory = new ServiceCollection()
        .AddSingleton(A.Fake<ICommandDispatcher>())
        .BuildServiceProvider()
        .GetRequiredService<IServiceScopeFactory>();

    public ParkedMessagesTests() {
        A.CallTo(() => _orders.Registration)
            .Returns(new SubscriptionRegistration("Orders@v2", typeof(object), typeof(OrderPlaced), false, ["Orders@v1"]));
        A.CallTo(() => _reminders.Registration)
            .Returns(new SubscriptionRegistration("Reminders@v1", typeof(object), typeof(object), false));
    }

    static CancellationToken Token => TestContext.Current.CancellationToken;

    ParkedMessages Messages(IReplayableSubscription? extra = null, IServiceScopeFactory? scopeFactory = null) =>
        new(extra is null ? [_orders, _reminders] : [_orders, _reminders, extra],
            _retryPolicy, scopeFactory ?? _scopeFactory, NullLogger<ParkedMessages>.Instance, _store);

    static IServiceScopeFactory ScopeFactory(ICommandDispatcher dispatcher) =>
        new ServiceCollection().AddSingleton(dispatcher).BuildServiceProvider().GetRequiredService<IServiceScopeFactory>();

    static SubscriptionMessage Message(ulong commitPosition, string stream = "order-1", ulong revision = 0) =>
        new(new OrderPlaced("order-1", "alice"), commitPosition, new EventReference(stream, revision), EventMetadata.Empty);

    static EventMetadata Metadata(string key, object value) =>
        new(new Dictionary<string, object?> { [key] = value });

    [Fact]
    public async Task ListAsync_ReturnsTheParkedMessages_OldestFirst() {
        _store.Park("Orders@v2", new SubscriptionMessage(new OrderPlaced("order-1", "alice"), 42, new EventReference("order-1", 7), EventMetadata.Empty),
            new InvalidOperationException("boom"));
        _store.Park("Orders@v2", new SubscriptionMessage(new OrderPlaced("order-2", "bob"), 43, new EventReference("order-2", 8), EventMetadata.Empty),
            new InvalidOperationException("bam"));

        var messages = await Messages().ListAsync("Orders@v2", Token);

        messages.Should().HaveCount(2);
        messages[0].Revision.Should().Be(0);
        messages[0].SubscriptionId.Should().Be("Orders@v2");
        messages[0].CommitPosition.Should().Be(42);
        messages[0].Source.Should().Be(new EventReference("order-1", 7));
        messages[0].EventType.Should().Be(typeof(OrderPlaced).FullName);
        messages[0].ExceptionType.Should().Be(typeof(InvalidOperationException).FullName);
        messages[0].ExceptionMessage.Should().Be("boom");
        messages[1].Revision.Should().Be(1);
        messages[1].CommitPosition.Should().Be(43);
        messages[1].Source.Should().Be(new EventReference("order-2", 8));
    }

    [Fact]
    public async Task ListAsync_LeavesOutReplayedAndDismissedMessages() {
        for (var i = 0; i < 3; i++)
            _store.Park("Orders@v2", new SubscriptionMessage(new OrderPlaced($"order-{i}", "alice"), (ulong)(42 + i), new EventReference($"order-{i}", (ulong)i), EventMetadata.Empty),
                new InvalidOperationException("boom"));
        _store.Append("Orders@v2", ParkedFormat.ReplayedEventType, new EventReference("parked-Orders@v2", 0).ToLinkData());
        _store.Append("Orders@v2", ParkedFormat.DismissedEventType, new EventReference("parked-Orders@v2", 2).ToLinkData());

        var messages = await Messages().ListAsync("Orders@v2", Token);

        messages.Should().ContainSingle().Which.Revision.Should().Be(1);
    }

    [Fact]
    public async Task ListAsync_IgnoresClosingEntriesOfAnotherStream() {
        _store.Park("Orders@v2", Message(42), new InvalidOperationException("boom"));
        _store.Append("Orders@v2", ParkedFormat.ReplayedEventType, new EventReference("parked-other", 0).ToLinkData());

        var messages = await Messages().ListAsync("Orders@v2", Token);

        messages.Should().ContainSingle().Which.Revision.Should().Be(0);
    }

    [Fact]
    public async Task ListAsync_OfAnUnknownId_IsEmpty() {
        var messages = await Messages().ListAsync("Unknown@v1", Token);

        messages.Should().BeEmpty();
    }

    [Fact]
    public async Task ReplayAsync_HandsTheEventToTheSubscriptionOfTheId_AndRecordsIt() {
        var source = new EventReference("order-1", 0);
        var metadata = Metadata("correlation", "c-1");
        var @event = new OrderPlaced("order-1", "alice");
        _store.Events[source] = new StoredEntry("IntegrationTests.OrderPlaced@v1", "{}"u8.ToArray(), metadata);
        _store.Deserializer = (_, _) => @event;
        _store.Park("Orders@v2", Message(42), new InvalidOperationException("boom"));

        var replay = await Messages().ReplayAsync("Orders@v2", 0, Token);

        replay.Succeeded.Should().BeTrue();
        A.CallTo(() => _orders.HandleAsync(A<SubscriptionMessage>.That.Matches(message =>
            ReferenceEquals(message.Event, @event) && message.Source == source && message.CommitPosition == 42 && ReferenceEquals(message.Metadata, metadata)), A<CancellationToken>._))
            .MustHaveHappenedOnceExactly();
        var entry = _store.Entries("Orders@v2").Should().ContainSingle(stored => stored.EventType == ParkedFormat.ReplayedEventType).Which;
        Encoding.UTF8.GetString(entry.Data.Span).Should().Be("0@parked-Orders@v2");
        (await Messages().ListAsync("Orders@v2", Token)).Should().BeEmpty();
    }

    [Fact]
    public async Task ReplayAsync_OfAPredecessor_GoesToItsSuccessor() {
        var source = new EventReference("order-1", 0);
        var @event = new OrderPlaced("order-1", "alice");
        _store.Events[source] = new StoredEntry("IntegrationTests.OrderPlaced@v1", "{}"u8.ToArray(), EventMetadata.Empty);
        _store.Deserializer = (_, _) => @event;
        _store.Park("Orders@v1", Message(42), new InvalidOperationException("boom"));

        var replay = await Messages().ReplayAsync("Orders@v1", 0, Token);

        replay.Succeeded.Should().BeTrue();
        A.CallTo(() => _orders.HandleAsync(A<SubscriptionMessage>._, A<CancellationToken>._)).MustHaveHappenedOnceExactly();
        A.CallTo(() => _reminders.HandleAsync(A<SubscriptionMessage>._, A<CancellationToken>._)).MustNotHaveHappened();
    }

    [Fact]
    public async Task ReplayAsync_RetriesAFailingHandler_AndKeepsTheMessage() {
        _store.Events[new EventReference("order-1", 0)] = new StoredEntry("IntegrationTests.OrderPlaced@v1", "{}"u8.ToArray(), EventMetadata.Empty);
        _store.Deserializer = (_, _) => new OrderPlaced("order-1", "alice");
        _store.Park("Orders@v2", Message(42), new InvalidOperationException("boom"));
        var failure = new InvalidOperationException("still failing");
        A.CallTo(() => _orders.HandleAsync(A<SubscriptionMessage>._, A<CancellationToken>._)).Throws(failure);

        var replay = await Messages().ReplayAsync("Orders@v2", 0, Token);

        replay.Succeeded.Should().BeFalse();
        replay.Error.Should().BeSameAs(failure);
        A.CallTo(() => _orders.HandleAsync(A<SubscriptionMessage>._, A<CancellationToken>._)).MustHaveHappened(2, Times.Exactly);
        _store.Entries("Orders@v2").Should().NotContain(entry => entry.EventType == ParkedFormat.ReplayedEventType);
        (await Messages().ListAsync("Orders@v2", Token)).Should().ContainSingle().Which.Revision.Should().Be(0);
    }

    [Fact]
    public async Task ReplayAsync_SucceedsAfterARetry() {
        _store.Events[new EventReference("order-1", 0)] = new StoredEntry("IntegrationTests.OrderPlaced@v1", "{}"u8.ToArray(), EventMetadata.Empty);
        _store.Deserializer = (_, _) => new OrderPlaced("order-1", "alice");
        _store.Park("Orders@v2", Message(42), new InvalidOperationException("boom"));
        A.CallTo(() => _orders.HandleAsync(A<SubscriptionMessage>._, A<CancellationToken>._)).Throws(new InvalidOperationException("transient")).Once();

        var replay = await Messages().ReplayAsync("Orders@v2", 0, Token);

        replay.Succeeded.Should().BeTrue();
    }

    [Fact]
    public async Task ReplayAsync_WithoutSource_FailsWithoutHandling() {
        _store.Append("Orders@v2", ParkedFormat.ParkedEventType,
            "{\"CommitPosition\":42,\"ExceptionType\":\"X\",\"Message\":\"m\"}"u8.ToArray());

        var replay = await Messages().ReplayAsync("Orders@v2", 0, Token);

        replay.Succeeded.Should().BeFalse();
        replay.Error!.Message.Should().Contain("only dismissed");
        A.CallTo(() => _orders.HandleAsync(A<SubscriptionMessage>._, A<CancellationToken>._)).MustNotHaveHappened();
    }

    [Fact]
    public async Task ReplayAsync_OfADeletedEvent_FailsWithoutHandling() {
        _store.Park("Orders@v2", Message(42), new InvalidOperationException("boom"));

        var replay = await Messages().ReplayAsync("Orders@v2", 0, Token);

        replay.Succeeded.Should().BeFalse();
        replay.Error!.Message.Should().Contain("doesn't exist anymore");
        A.CallTo(() => _orders.HandleAsync(A<SubscriptionMessage>._, A<CancellationToken>._)).MustNotHaveHappened();
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task ReplayAsync_OfAnUndeserializableEvent_Fails(bool deserializerThrows) {
        var source = new EventReference("order-1", 0);
        _store.Events[source] = new StoredEntry("IntegrationTests.OrderPlaced@v1", "{}"u8.ToArray(), EventMetadata.Empty);
        var failure = new InvalidOperationException("broken payload");
        _store.Deserializer = deserializerThrows
            ? (_, _) => throw failure
            : (_, _) => null;
        _store.Park("Orders@v2", Message(42), new InvalidOperationException("boom"));

        var replay = await Messages().ReplayAsync("Orders@v2", 0, Token);

        replay.Succeeded.Should().BeFalse();
        if (deserializerThrows) {
            replay.Error!.Message.Should().Be($"{source} can't be deserialized.");
            replay.Error.InnerException.Should().BeSameAs(failure);
        } else {
            replay.Error!.Message.Should().Contain("doesn't know the type");
        }
        A.CallTo(() => _orders.HandleAsync(A<SubscriptionMessage>._, A<CancellationToken>._)).MustNotHaveHappened();
    }

    [Fact]
    public async Task ReplayAsync_OfAnEventTheSubscriptionDoesNotHandle_Fails() {
        _store.Events[new EventReference("order-1", 0)] = new StoredEntry("IntegrationTests.NotPlaced@v1", "{}"u8.ToArray(), EventMetadata.Empty);
        _store.Deserializer = (_, _) => "a string";
        _store.Park("Orders@v2", Message(42), new InvalidOperationException("boom"));

        var replay = await Messages().ReplayAsync("Orders@v2", 0, Token);

        replay.Succeeded.Should().BeFalse();
        replay.Error!.Message.Should().Contain("doesn't handle");
        A.CallTo(() => _orders.HandleAsync(A<SubscriptionMessage>._, A<CancellationToken>._)).MustNotHaveHappened();
    }

    [Fact]
    public async Task ReplayAsync_WithoutASubscriptionInThisHost_Fails() {
        _store.Events[new EventReference("order-1", 0)] = new StoredEntry("IntegrationTests.OrderPlaced@v1", "{}"u8.ToArray(), EventMetadata.Empty);
        _store.Park("Unknown@v1", Message(42), new InvalidOperationException("boom"));

        var replay = await Messages().ReplayAsync("Unknown@v1", 0, Token);

        replay.Succeeded.Should().BeFalse();
        replay.Error!.Message.Should().Contain("No subscription in this host");
    }

    [Fact]
    public async Task ReplayAsync_WithTwoSubscriptionsForTheId_Fails() {
        var third = A.Fake<IReplayableSubscription>();
        A.CallTo(() => third.Registration)
            .Returns(new SubscriptionRegistration("Orders@v3", typeof(object), typeof(OrderPlaced), false, ["Orders@v1"]));
        _store.Events[new EventReference("order-1", 0)] = new StoredEntry("IntegrationTests.OrderPlaced@v1", "{}"u8.ToArray(), EventMetadata.Empty);
        _store.Park("Orders@v1", Message(42), new InvalidOperationException("boom"));

        var replay = await Messages(third).ReplayAsync("Orders@v1", 0, Token);

        replay.Succeeded.Should().BeFalse();
        replay.Error!.Message.Should().Contain("2 subscriptions");
    }

    [Theory]
    [InlineData(9UL)]
    [InlineData(0UL)]
    public async Task ReplayAsync_OfANotListedMessage_Throws(ulong revision) {
        _store.Park("Orders@v2", Message(42), new InvalidOperationException("boom"));
        _store.Append("Orders@v2", ParkedFormat.ReplayedEventType, new EventReference("parked-Orders@v2", 0).ToLinkData());

        var act = () => Messages().ReplayAsync("Orders@v2", revision, Token).AsTask();

        await act.Should().ThrowAsync<InvalidOperationException>().WithMessage("*is not listed*");
    }

    [Fact]
    public async Task ReplayAsync_OfADeferredCommand_DispatchesIt_WithTheMetadataOfTheRequest() {
        var source = new EventReference("scheduled-requests-x", 0);
        var due = new DateTimeOffset(2026, 10, 16, 8, 0, 0, TimeSpan.Zero);
        var metadata = Metadata("correlation", "c-1");
        _store.Events[source] = new StoredEntry(ScheduledFormat.CommandRequestEventType,
            ScheduledFormat.WriteRequest("Reminders@v1", due, "Test.Command@v1", "{}"u8), metadata);
        var command = new NoopCommand();
        _store.Deserializer = (_, _) => command;
        _store.Park("scheduled-Reminders@v1", new SubscriptionMessage(null, 42, source, EventMetadata.Empty), new InvalidOperationException("boom"));
        var dispatcher = A.Fake<ICommandDispatcher>();
        string? correlation = null;
        A.CallTo(() => dispatcher.DispatchAsync(command, A<CancellationToken>._))
            .Invokes(_ => correlation = (string?)MetadataScope.Current?.Snapshot()["correlation"]);

        var replay = await Messages(scopeFactory: ScopeFactory(dispatcher)).ReplayAsync("scheduled-Reminders@v1", 0, Token);

        replay.Succeeded.Should().BeTrue();
        A.CallTo(() => dispatcher.DispatchAsync(command, A<CancellationToken>._)).MustHaveHappenedOnceExactly();
        correlation.Should().Be("c-1");
    }

    [Fact]
    public async Task ReplayAsync_OfADeadline_DeliversItToTheSagaWhoseSchedulerParkedIt() {
        var source = new EventReference("saga-1", 3);
        var due = new DateTimeOffset(2026, 10, 16, 8, 0, 0, TimeSpan.Zero);
        var metadata = Metadata("correlation", "c-1");
        _store.Events[source] = new StoredEntry(ScheduledFormat.DeadlineRequestEventType,
            ScheduledFormat.WriteRequest("Reminders@v1", due, "Test.Deadline@v1", "{}"u8), metadata);
        var payload = "overdue";
        _store.Deserializer = (_, _) => payload;
        _store.Park("scheduled-Reminders@v1", new SubscriptionMessage(null, 42, source, EventMetadata.Empty), new InvalidOperationException("boom"));

        var replay = await Messages().ReplayAsync("scheduled-Reminders@v1", 0, Token);

        replay.Succeeded.Should().BeTrue();
        A.CallTo(() => _reminders.HandleDeadlineAsync(A<ScheduledDeadline>.That.Matches(deadline =>
            deadline.Request == source && deadline.Due == due && ReferenceEquals(deadline.Payload, payload) &&
            ReferenceEquals(deadline.Metadata, metadata) && deadline.CommitPosition == 42), A<CancellationToken>._))
            .MustHaveHappenedOnceExactly();
    }

    [Fact]
    public async Task ReplayAsync_OfARequestWithoutACommand_Fails() {
        var source = new EventReference("scheduled-requests-x", 0);
        var due = new DateTimeOffset(2026, 10, 16, 8, 0, 0, TimeSpan.Zero);
        _store.Events[source] = new StoredEntry(ScheduledFormat.CommandRequestEventType,
            ScheduledFormat.WriteRequest("Reminders@v1", due, "Test.Command@v1", "{}"u8), EventMetadata.Empty);
        _store.Deserializer = (_, _) => "not a command";
        _store.Park("scheduled-Reminders@v1", new SubscriptionMessage(null, 42, source, EventMetadata.Empty), new InvalidOperationException("boom"));

        var replay = await Messages().ReplayAsync("scheduled-Reminders@v1", 0, Token);

        replay.Succeeded.Should().BeFalse();
        replay.Error!.Message.Should().Contain("which is not a command");
    }

    [Fact]
    public async Task ReplayAllAsync_ReplaysInOrder_AndStopsAtTheFirstFailure() {
        for (ulong i = 0; i < 3; i++) {
            _store.Events[new EventReference("order-1", i)] = new StoredEntry("IntegrationTests.OrderPlaced@v1", "{}"u8.ToArray(), EventMetadata.Empty);
            _store.Park("Orders@v2", new SubscriptionMessage(new OrderPlaced($"order-{i}", "alice"), 42 + i, new EventReference("order-1", i), EventMetadata.Empty),
                new InvalidOperationException("boom"));
        }
        _store.Deserializer = (_, _) => new OrderPlaced("order-1", "alice");
        var failure = new InvalidOperationException("still failing");
        A.CallTo(() => _orders.HandleAsync(A<SubscriptionMessage>.That.Matches(message => message.Source.Revision == 1), A<CancellationToken>._))
            .Throws(failure);

        var replays = await Messages().ReplayAllAsync("Orders@v2", Token);

        replays.Should().HaveCount(2);
        replays[0].Succeeded.Should().BeTrue();
        replays[0].Message.Revision.Should().Be(0);
        replays[1].Succeeded.Should().BeFalse();
        replays[1].Message.Revision.Should().Be(1);
        replays[1].Error.Should().BeSameAs(failure);
        A.CallTo(() => _orders.HandleAsync(A<SubscriptionMessage>._, A<CancellationToken>._)).MustHaveHappened(3, Times.Exactly);
        (await Messages().ListAsync("Orders@v2", Token)).Select(message => message.Revision).Should().Equal(1UL, 2UL);
    }

    [Fact]
    public async Task DismissAsync_RecordsTheDismissal_WithoutHandling() {
        _store.Park("Orders@v2", Message(42), new InvalidOperationException("boom"));

        await Messages().DismissAsync("Orders@v2", 0, Token);

        var entry = _store.Entries("Orders@v2").Should().ContainSingle(stored => stored.EventType == ParkedFormat.DismissedEventType).Which;
        Encoding.UTF8.GetString(entry.Data.Span).Should().Be("0@parked-Orders@v2");
        (await Messages().ListAsync("Orders@v2", Token)).Should().BeEmpty();
        A.CallTo(() => _orders.HandleAsync(A<SubscriptionMessage>._, A<CancellationToken>._)).MustNotHaveHappened();
    }

    [Fact]
    public async Task DismissAsync_OfANotListedMessage_Throws() {
        _store.Park("Orders@v2", Message(42), new InvalidOperationException("boom"));

        var act = () => Messages().DismissAsync("Orders@v2", 9, Token).AsTask();

        await act.Should().ThrowAsync<InvalidOperationException>().WithMessage("*is not listed*");
    }

    [Fact]
    public async Task WithoutAStore_Throws() {
        var messages = new ParkedMessages([_orders, _reminders], _retryPolicy, _scopeFactory, NullLogger<ParkedMessages>.Instance);

        var act = () => messages.ListAsync("Orders@v2", Token).AsTask();

        await act.Should().ThrowAsync<InvalidOperationException>().WithMessage("*No parked-message store*");
    }
}
