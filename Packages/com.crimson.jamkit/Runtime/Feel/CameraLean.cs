using UnityEngine;

namespace JamKit
{
    /// <summary>
    /// Acceleration-driven camera roll ("lean into the turn"). Drive it from any controller: call
    /// <see cref="Initialize"/> once, then <see cref="UpdateLean"/> each frame with the current acceleration.
    /// </summary>
    /// <remarks>
    /// Extracted from ProjectFPS's <c>CameraLean</c>. The donor took a <c>CharacterState</c> (stance/grounded);
    /// per the audit's decoupling note this now takes a plain <see cref="Vector3"/> acceleration plus an optional
    /// strength multiplier, so it works for any mover (the caller supplies a larger multiplier while sliding/
    /// boosting, etc.). Keeps the asymmetric attack/decay damping and framerate-independent strength smoothing.
    /// </remarks>
    public class CameraLean : MonoBehaviour
    {
        [SerializeField] private float _attackingDamping = 0.5f;
        [SerializeField] private float _decayDamping = 0.3f;

        [Tooltip("Base lean strength (radians of roll per unit of damped acceleration), before the per-call multiplier.")]
        [SerializeField] private float _strength = 0.075f;

        [Tooltip("How fast the smoothed strength tracks the target strength (1 - exp response rate).")]
        [SerializeField] private float _strengthResponse = 5f;

        private Vector3 _dampedAcceleration;
        private Vector3 _dampedAccelerationVel;
        private float _smoothStrength;

        /// <summary>Seed the smoothed strength at the base strength.</summary>
        public void Initialize()
        {
            _smoothStrength = _strength;
        }

        /// <summary>
        /// Advance and apply the lean. <paramref name="acceleration"/> is the mover's world-space acceleration;
        /// <paramref name="up"/> is the world up; <paramref name="strengthMultiplier"/> scales the base strength
        /// (for example a larger value while sliding).
        /// </summary>
        public void UpdateLean(float deltaTime, Vector3 acceleration, Vector3 up, float strengthMultiplier = 1f)
        {
            Vector3 planarAcceleration = Vector3.ProjectOnPlane(acceleration, up);
            float damping = planarAcceleration.magnitude > _dampedAcceleration.magnitude ? _attackingDamping : _decayDamping;

            _dampedAcceleration = Vector3.SmoothDamp
            (
                current: _dampedAcceleration,
                target: planarAcceleration,
                currentVelocity: ref _dampedAccelerationVel,
                smoothTime: damping,
                maxSpeed: float.PositiveInfinity,
                deltaTime: deltaTime
            );

            Vector3 leanAxis = Vector3.Cross(_dampedAcceleration.normalized, up).normalized;

            transform.localRotation = Quaternion.identity;

            float targetStrength = _strength * strengthMultiplier;
            _smoothStrength = MathUtil.Decay(_smoothStrength, targetStrength, _strengthResponse, deltaTime);

            transform.rotation = Quaternion.AngleAxis(_dampedAcceleration.magnitude * _smoothStrength, leanAxis) * transform.rotation;
        }
    }
}
