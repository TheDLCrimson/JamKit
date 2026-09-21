using NUnit.Framework;
using UnityEngine.TestTools;

namespace JamKit.Tests
{
    public class EventBusTests
    {
        private struct TestEvent : IGameEvent
        {
            public int Value;
        }

        private struct OtherTestEvent : IGameEvent { }

        [TearDown]
        public void TearDown()
        {
            // EventBus is static; clear it so bindings never leak between tests.
            EventBus.Clear();
        }

        [Test]
        public void Subscribe_Fire_InvokesCallbackWithPayload()
        {
            int received = 0;
            EventBus.Subscribe<TestEvent>(e => received = e.Value);

            EventBus.Fire(new TestEvent { Value = 42 });

            Assert.AreEqual(42, received);
        }

        [Test]
        public void Fire_WithNoSubscribers_DoesNotThrow()
        {
            Assert.DoesNotThrow(() => EventBus.Fire(new TestEvent { Value = 1 }));
        }

        [Test]
        public void Unsubscribe_StopsReceivingEvents()
        {
            int callCount = 0;
            void Handler(TestEvent e) => callCount++;

            EventBus.Subscribe<TestEvent>(Handler);
            EventBus.Fire(new TestEvent());
            EventBus.Unsubscribe<TestEvent>(Handler);
            EventBus.Fire(new TestEvent());

            Assert.AreEqual(1, callCount);
        }

        [Test]
        public void Clear_RemovesSubscribersForAllEventTypes()
        {
            int testEventCount = 0;
            int otherEventCount = 0;
            EventBus.Subscribe<TestEvent>(_ => testEventCount++);
            EventBus.Subscribe<OtherTestEvent>(_ => otherEventCount++);

            EventBus.Clear();
            EventBus.Fire(new TestEvent());
            EventBus.Fire(new OtherTestEvent());

            Assert.AreEqual(0, testEventCount);
            Assert.AreEqual(0, otherEventCount);
        }

        [Test]
        public void ClearOfT_RemovesOnlyThatEventType()
        {
            int testEventCount = 0;
            int otherEventCount = 0;
            EventBus.Subscribe<TestEvent>(_ => testEventCount++);
            EventBus.Subscribe<OtherTestEvent>(_ => otherEventCount++);

            EventBus.Clear<TestEvent>();
            EventBus.Fire(new TestEvent());
            EventBus.Fire(new OtherTestEvent());

            Assert.AreEqual(0, testEventCount);
            Assert.AreEqual(1, otherEventCount);
        }

        [Test]
        public void Fire_ExceptionInOneSubscriber_StillInvokesRemainingSubscribers()
        {
            bool secondSubscriberRan = false;
            EventBus.Subscribe<TestEvent>(_ => throw new System.Exception("deliberate test exception"));
            EventBus.Subscribe<TestEvent>(_ => secondSubscriberRan = true);

            LogAssert.Expect(UnityEngine.LogType.Error, new System.Text.RegularExpressions.Regex("deliberate test exception"));
            EventBus.Fire(new TestEvent());

            Assert.IsTrue(secondSubscriberRan);
        }

        [Test]
        public void Fire_SubscriberUnsubscribingAnotherDuringDispatch_DoesNotThrowAndRemainingStillRun()
        {
            bool secondRan = false;
            void Second(TestEvent e) => secondRan = true;

            // First subscriber unsubscribes the second one while the event is being dispatched.
            EventBus.Subscribe<TestEvent>(_ => EventBus.Unsubscribe<TestEvent>(Second));
            EventBus.Subscribe<TestEvent>(Second);

            // Copy-on-fire: Second was registered when this Fire began, so it still runs this dispatch.
            Assert.DoesNotThrow(() => EventBus.Fire(new TestEvent()));
            Assert.IsTrue(secondRan);

            // ...but the unsubscribe took effect for subsequent fires.
            secondRan = false;
            EventBus.Fire(new TestEvent());
            Assert.IsFalse(secondRan);
        }

        [Test]
        public void Fire_ClearDuringDispatch_DoesNotThrowAndBusIsEmptyAfterward()
        {
            bool secondRan = false;

            // A subscriber clears the whole bus mid-dispatch (the realistic case: a callback triggers a
            // scene load, which calls EventBus.Clear()).
            EventBus.Subscribe<TestEvent>(_ => EventBus.Clear());
            EventBus.Subscribe<TestEvent>(_ => secondRan = true);

            // Copy-on-fire: the second subscriber, registered before the fire, still runs this dispatch.
            Assert.DoesNotThrow(() => EventBus.Fire(new TestEvent()));
            Assert.IsTrue(secondRan);

            // The bus is empty for subsequent fires.
            Assert.AreEqual(0, EventBus.GetSubscriberCount<TestEvent>());
            secondRan = false;
            EventBus.Fire(new TestEvent());
            Assert.IsFalse(secondRan);
        }

        [Test]
        public void GetSubscriberCount_And_HasSubscribers_ReflectCurrentBindings()
        {
            Assert.AreEqual(0, EventBus.GetSubscriberCount<TestEvent>());
            Assert.IsFalse(EventBus.HasSubscribers<TestEvent>());

            void Handler(TestEvent e) { }
            EventBus.Subscribe<TestEvent>(Handler);

            Assert.AreEqual(1, EventBus.GetSubscriberCount<TestEvent>());
            Assert.IsTrue(EventBus.HasSubscribers<TestEvent>());

            EventBus.Unsubscribe<TestEvent>(Handler);

            Assert.AreEqual(0, EventBus.GetSubscriberCount<TestEvent>());
            Assert.IsFalse(EventBus.HasSubscribers<TestEvent>());
        }
    }
}
