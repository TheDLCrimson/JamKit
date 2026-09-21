using UnityEngine;

namespace JamKit
{
    /// <summary>
    /// Frame-rate independent smoothing helpers built on the <c>1 - exp(-k * dt)</c> exponential decay idiom.
    /// Prefer these over raw <c>Lerp(a, b, k)</c>, which is frame-rate dependent.
    /// </summary>
    public static class MathUtil
    {
        /// <summary>
        /// The Lerp factor for one step of exponential decay toward a target, at rate <paramref name="k"/>
        /// over <paramref name="dt"/> seconds. Higher <paramref name="k"/> reaches the target faster.
        /// </summary>
        public static float Decay(float k, float dt)
        {
            return 1f - Mathf.Exp(-k * dt);
        }

        /// <summary>
        /// Decays <paramref name="current"/> toward <paramref name="target"/> at rate <paramref name="k"/>.
        /// </summary>
        public static float Decay(float current, float target, float k, float dt)
        {
            return Mathf.Lerp(current, target, Decay(k, dt));
        }

        /// <summary>
        /// Decays <paramref name="current"/> toward <paramref name="target"/> at rate <paramref name="k"/>.
        /// </summary>
        public static Vector2 Decay(Vector2 current, Vector2 target, float k, float dt)
        {
            return Vector2.Lerp(current, target, Decay(k, dt));
        }

        /// <summary>
        /// Decays <paramref name="current"/> toward <paramref name="target"/> at rate <paramref name="k"/>.
        /// </summary>
        public static Vector3 Decay(Vector3 current, Vector3 target, float k, float dt)
        {
            return Vector3.Lerp(current, target, Decay(k, dt));
        }
    }
}
