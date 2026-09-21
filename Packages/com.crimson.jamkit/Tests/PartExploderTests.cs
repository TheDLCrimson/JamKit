using System.Reflection;
using NUnit.Framework;
using UnityEngine;

namespace JamKit.Tests
{
    public class PartExploderTests
    {
        // WorldSizeToLocalSize is private - a pure unit-space conversion, not behavior worth widening
        // PartExploder's public API for. The coroutine it feeds (BreakApart) needs Play Mode to actually
        // run, so this isolates and directly verifies the fixed conversion logic instead.
        private static Vector3 Invoke(Vector3 worldSize, Vector3 lossyScale)
        {
            MethodInfo method = typeof(PartExploder).GetMethod("WorldSizeToLocalSize", BindingFlags.NonPublic | BindingFlags.Static);
            return (Vector3)method.Invoke(null, new object[] { worldSize, lossyScale });
        }

        [Test]
        public void WorldSizeToLocalSize_UnitScale_ReturnsWorldSizeUnchanged()
        {
            Vector3 result = Invoke(new Vector3(2f, 3f, 4f), Vector3.one);

            Assert.AreEqual(new Vector3(2f, 3f, 4f), result);
        }

        [Test]
        public void WorldSizeToLocalSize_ScaledUpPart_DividesOutTheScale()
        {
            // A part with local scale (2,2,2) whose world-space renderer bounds measure (4,4,4) has a true
            // local size of (2,2,2) - the previous bug assigned (4,4,4) directly, doubling the collider.
            Vector3 result = Invoke(new Vector3(4f, 4f, 4f), new Vector3(2f, 2f, 2f));

            Assert.AreEqual(new Vector3(2f, 2f, 2f), result);
        }

        [Test]
        public void WorldSizeToLocalSize_ScaledDownPart_DividesOutTheScale()
        {
            Vector3 result = Invoke(new Vector3(1f, 1f, 1f), new Vector3(0.5f, 0.5f, 0.5f));

            Assert.AreEqual(new Vector3(2f, 2f, 2f), result);
        }

        [Test]
        public void WorldSizeToLocalSize_ZeroScaleAxis_FallsBackToWorldSizeInsteadOfDividingByZero()
        {
            Assert.DoesNotThrow(() => Invoke(Vector3.one, new Vector3(0f, 1f, 1f)));

            Vector3 result = Invoke(new Vector3(2f, 3f, 4f), new Vector3(0f, 1f, 1f));

            Assert.AreEqual(2f, result.x);
            Assert.IsFalse(float.IsNaN(result.x));
            Assert.IsFalse(float.IsInfinity(result.x));
        }
    }
}
