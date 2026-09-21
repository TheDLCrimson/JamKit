using System.Collections.Generic;
using System.Reflection;
using System.Text.RegularExpressions;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;

namespace JamKit.Tests
{
    public class UIBlockerStackTests
    {
        // EditMode tests run against whatever scene is currently open in the Editor, not an isolated one -
        // this repo's Game.unity has its own real UIBlockerStack, which would otherwise already occupy
        // Singleton<UIBlockerStack>'s static _instance slot and make every test's own instance look like a
        // duplicate. Clearing it in SetUp/TearDown makes each test hermetic; Instance self-heals back to
        // whatever's really in the scene on its next read (Singleton<T>'s own null-safe re-resolve), so
        // nothing outside this test is left in a broken state.
        //
        // AddComponent's automatic Awake dispatch is not reliably synchronous in this EditMode test
        // environment (observed directly: querying state immediately, and even across separate calls, shows
        // Awake not yet having run). Every UIBlockerStack instance this file creates therefore has Awake
        // invoked explicitly via InvokeAwake rather than trusting engine timing. Awake itself is idempotent
        // (the container-null-check and ??= assignments no-op on a second call), so this is safe even if
        // Unity's own dispatch also eventually fires it.
        private static readonly Regex DestroyInEditModeWarning = new Regex("Destroy may not be called from edit mode");

        private class TestBlocker : UIBlockerBase
        {
            public int ResumeCount;
            public int CloseCount;
            public override void OnResume() => ResumeCount++;
            public override void OnClose() => CloseCount++;
        }

        private GameObject _stackHost;
        private UIBlockerStack _stack;
        private GameObject _prefabA;
        private GameObject _prefabB;
        private GameObject _prefabC;

        [SetUp]
        public void SetUp()
        {
            ResetSingletonInstance();

            _stackHost = new GameObject(nameof(UIBlockerStack));
            _stack = _stackHost.AddComponent<UIBlockerStack>();
            InvokeAwake(_stack);

            _prefabA = CreatePrefab("A");
            _prefabB = CreatePrefab("B");
            _prefabC = CreatePrefab("C");
        }

        [TearDown]
        public void TearDown()
        {
            Object.DestroyImmediate(_stackHost);
            Object.DestroyImmediate(_prefabA);
            Object.DestroyImmediate(_prefabB);
            Object.DestroyImmediate(_prefabC);

            ResetSingletonInstance();
        }

        private static void ResetSingletonInstance()
        {
            FieldInfo field = typeof(Singleton<UIBlockerStack>).GetField("_instance", BindingFlags.NonPublic | BindingFlags.Static);
            field.SetValue(null, null);
        }

        private static void InvokeAwake(UIBlockerStack stack)
        {
            MethodInfo method = typeof(UIBlockerStack).GetMethod("Awake", BindingFlags.NonPublic | BindingFlags.Instance);
            method.Invoke(stack, null);
        }

        private static GameObject CreatePrefab(string name)
        {
            var go = new GameObject(name, typeof(RectTransform));
            go.AddComponent<TestBlocker>();
            return go;
        }

        // Test-only reflection into the private blocker stack: OpenBlocker deliberately returns only a
        // bool (see the class remarks / M6 friction log), so this is how the test captures the instantiated
        // clone rather than the prefab passed in.
        private UIBlockerBase PeekTop()
        {
            FieldInfo field = typeof(UIBlockerStack).GetField("_blockerStack", BindingFlags.NonPublic | BindingFlags.Instance);
            var stack = (Stack<UIBlockerBase>)field.GetValue(_stack);
            return stack.Count > 0 ? stack.Peek() : null;
        }

        // Every CloseTopBlocker()/CloseBlocker() call Destroy()s each blocker it closes - correct for real
        // (Play Mode / built player) use, but Destroy() is a no-op-with-a-logged-error outside Play Mode,
        // which these EditMode tests are. Expect exactly one such error per blocker closed.
        private static void ExpectDestroyWarnings(int count)
        {
            for (int i = 0; i < count; i++)
            {
                LogAssert.Expect(LogType.Error, DestroyInEditModeWarning);
            }
        }

        [Test]
        public void CloseTopBlocker_RevealsNewTop_FiresOnResumeExactlyOnce()
        {
            _stack.OpenBlocker(_prefabA.GetComponent<TestBlocker>());
            var instanceA = (TestBlocker)PeekTop();
            _stack.OpenBlocker(_prefabB.GetComponent<TestBlocker>());

            ExpectDestroyWarnings(1); // closes B
            _stack.CloseTopBlocker();

            Assert.AreEqual(1, instanceA.ResumeCount);
        }

        [Test]
        public void CloseBlocker_OnNonTopTarget_DoesNotFireSpuriousOnResumeOnIntermediateBlockers()
        {
            _stack.OpenBlocker(_prefabA.GetComponent<TestBlocker>());
            var instanceA = (TestBlocker)PeekTop();
            _stack.OpenBlocker(_prefabB.GetComponent<TestBlocker>());
            var instanceB = (TestBlocker)PeekTop();
            _stack.OpenBlocker(_prefabC.GetComponent<TestBlocker>());
            var instanceC = (TestBlocker)PeekTop();

            ExpectDestroyWarnings(3); // closes C, B, A
            bool closed = _stack.CloseBlocker(instanceA);

            Assert.IsTrue(closed);
            Assert.AreEqual(0, instanceB.ResumeCount,
                "B is closed in the same batch as the target and must not receive a spurious OnResume before being destroyed.");
            Assert.AreEqual(0, instanceA.ResumeCount);
            Assert.AreEqual(1, instanceA.CloseCount);
            Assert.AreEqual(1, instanceB.CloseCount);
            Assert.AreEqual(1, instanceC.CloseCount);
            Assert.AreEqual(0, _stack.GetBlockerCount());
        }

        [Test]
        public void CloseBlocker_RevealsWhatRemainsBeneath_AfterBatchClose()
        {
            _stack.OpenBlocker(_prefabA.GetComponent<TestBlocker>());
            var instanceA = (TestBlocker)PeekTop();
            _stack.OpenBlocker(_prefabB.GetComponent<TestBlocker>());
            var instanceB = (TestBlocker)PeekTop();
            _stack.OpenBlocker(_prefabC.GetComponent<TestBlocker>());

            // Close B (and everything above it, i.e. C), leaving A as the new top.
            ExpectDestroyWarnings(2); // closes C, B
            _stack.CloseBlocker(instanceB);

            Assert.AreEqual(1, instanceA.ResumeCount, "A is revealed exactly once after the batch close finishes.");
            Assert.AreEqual(1, _stack.GetBlockerCount());
        }

        [Test]
        public void Awake_OnDuplicateInstance_SkipsBlockerContainerAllocation()
        {
            var second = new GameObject("Second").AddComponent<UIBlockerStack>();

            // The second instance's base.Awake() (Singleton<T>) calls Destroy(gameObject) on itself; Destroy
            // is a no-op outside Play Mode and logs this exact warning instead (verified live against this
            // Unity version before writing this expectation).
            ExpectDestroyWarnings(1);
            InvokeAwake(second);

            Assert.AreEqual(1, _stackHost.transform.childCount,
                "The surviving instance (created in SetUp) should have auto-created its BlockerContainer.");
            Assert.AreEqual(0, second.transform.childCount,
                "The duplicate instance must not allocate a BlockerContainer it will never use.");

            Object.DestroyImmediate(second.gameObject);
        }
    }
}
