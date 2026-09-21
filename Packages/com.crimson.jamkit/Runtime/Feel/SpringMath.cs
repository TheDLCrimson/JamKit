using UnityEngine;

namespace JamKit
{
    /// <summary>
    /// Stateless procedural-motion math. Hosts the implicit-Euler damped harmonic spring solver hoisted out of
    /// ProjectFPS's <c>CameraSpring</c> (where it was an instance method) so any system - camera, UI, recoil,
    /// follow-cams - can reuse it.
    /// </summary>
    public static class SpringMath
    {
        /// <summary>
        /// Advances a damped harmonic spring toward <paramref name="target"/> by one step, mutating
        /// <paramref name="current"/> and <paramref name="velocity"/> in place. Unconditionally stable (implicit
        /// Euler), tuned by <paramref name="halfLife"/> (time to halve the displacement) and
        /// <paramref name="frequency"/> (oscillation rate).
        /// </summary>
        public static void Spring(ref Vector3 current, ref Vector3 velocity, Vector3 target, float halfLife, float frequency, float timeStep)
        {
            float dampingRatio = -Mathf.Log(0.5f) / (frequency * halfLife);
            float f = 1.0f + 2.0f * timeStep * dampingRatio * frequency;
            float oo = frequency * frequency;
            float hoo = timeStep * oo;
            float hhoo = timeStep * hoo;
            float detInv = 1.0f / (f + hhoo);
            Vector3 detX = f * current + timeStep * velocity + hhoo * target;
            Vector3 detV = velocity + hoo * (target - current);
            current = detX * detInv;
            velocity = detV * detInv;
        }
    }
}
