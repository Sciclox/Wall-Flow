using System;
using System.Collections.Concurrent;
using System.Threading.Tasks;
using WallFlow.Services;
using Xunit;

namespace WallFlow.Tests;

public class EventBusTests
{
    private class TestEvent : IEvent
    {
        public string Message { get; }
        public TestEvent(string message) => Message = message;
    }

    private class AnotherTestEvent : IEvent { }

    [Fact]
    public void EventBus_SubscribeAndPublish_DeliversPayload()
    {
        string? received = null;
        Action<TestEvent> handler = e => received = e.Message;

        EventBus.Subscribe(handler);
        EventBus.Publish(new TestEvent("Hello WallFlow"));

        Assert.Equal("Hello WallFlow", received);

        EventBus.Unsubscribe(handler);
    }

    [Fact]
    public void EventBus_Unsubscribe_StopsDelivery()
    {
        int count = 0;
        Action<TestEvent> handler = e => count++;

        EventBus.Subscribe(handler);
        EventBus.Publish(new TestEvent("First"));
        Assert.Equal(1, count);

        EventBus.Unsubscribe(handler);
        EventBus.Publish(new TestEvent("Second"));
        Assert.Equal(1, count);
    }

    [Fact]
    public void EventBus_ConcurrentPublish_IsThreadSafe()
    {
        var bag = new ConcurrentBag<string>();
        Action<TestEvent> handler = e => bag.Add(e.Message);

        EventBus.Subscribe(handler);

        Parallel.For(0, 100, i =>
        {
            EventBus.Publish(new TestEvent($"msg_{i}"));
        });

        EventBus.Unsubscribe(handler);

        Assert.Equal(100, bag.Count);
    }

    [Fact]
    public void EventBus_DifferentEventTypes_AreIsolated()
    {
        bool testEventReceived = false;
        bool anotherReceived = false;

        Action<TestEvent> handler1 = e => testEventReceived = true;
        Action<AnotherTestEvent> handler2 = e => anotherReceived = true;

        EventBus.Subscribe(handler1);
        EventBus.Subscribe(handler2);

        EventBus.Publish(new AnotherTestEvent());

        Assert.False(testEventReceived);
        Assert.True(anotherReceived);

        EventBus.Unsubscribe(handler1);
        EventBus.Unsubscribe(handler2);
    }

    private class SampleRecipient
    {
        public string? ReceivedMessage { get; set; }
        public void HandleTest(TestEvent e) => ReceivedMessage = e.Message;
    }

    [Fact]
    public void EventBus_RecipientSubscribeAndPublish_DeliversPayload()
    {
        var recipient = new SampleRecipient();
        EventBus.Subscribe<SampleRecipient, TestEvent>(recipient, (r, e) => r.HandleTest(e));

        EventBus.Publish(new TestEvent("Messenger Payload"));

        Assert.Equal("Messenger Payload", recipient.ReceivedMessage);

        EventBus.Unsubscribe<SampleRecipient, TestEvent>(recipient);
        EventBus.Publish(new TestEvent("Second Payload"));

        Assert.Equal("Messenger Payload", recipient.ReceivedMessage);
    }

    [Fact]
    public void EventBus_UnifiedGalleryEvents_DeliverThroughBusAndBridge()
    {
        bool busRefreshed = false;
        string? busNewDir = null;
        bool bridgeRefreshed = false;
        string? bridgeNewDir = null;

        Action<GalleryRefreshRequestedEvent> busRefreshHandler = _ => busRefreshed = true;
        Action<WallpaperDirectoryChangedEvent> busDirHandler = e => busNewDir = e.NewDirectory;
        Action bridgeRefreshHandler = () => bridgeRefreshed = true;
        Action<string> bridgeDirHandler = d => bridgeNewDir = d;

        EventBus.Subscribe(busRefreshHandler);
        EventBus.Subscribe(busDirHandler);
        ApplicationEvents.RefreshRequested += bridgeRefreshHandler;
        ApplicationEvents.DirectoryChanged += bridgeDirHandler;

        ApplicationEvents.NotifyRefreshRequested();
        ApplicationEvents.NotifyDirectoryChanged(@"C:\Wallpapers\Cyberpunk");

        Assert.True(busRefreshed);
        Assert.True(bridgeRefreshed);
        Assert.Equal(@"C:\Wallpapers\Cyberpunk", busNewDir);
        Assert.Equal(@"C:\Wallpapers\Cyberpunk", bridgeNewDir);

        EventBus.Unsubscribe(busRefreshHandler);
        EventBus.Unsubscribe(busDirHandler);
        ApplicationEvents.RefreshRequested -= bridgeRefreshHandler;
        ApplicationEvents.DirectoryChanged -= bridgeDirHandler;
    }
}
