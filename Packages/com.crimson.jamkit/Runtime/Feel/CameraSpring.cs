using UnityEngine;

namespace JamKit
{
    /// <summary>
    /// Procedural positional-lag + pitch-kick spring for a camera (landing thud / impact bob). Drive it from a
    /// controller: call <see cref="Initialize"/> once, then <see cref="UpdateSpring"/> each frame with the
    /// world up vector.
    /// </summary>
    /// <remarks>
    /// Extracted from ProjectFPS's <c>CameraSpring</c>; the spring solver now lives in <see cref="SpringMath"/>.
    /// Needs only a parent transform and an up vector - no character-controller coupling.
    /// </remarks>
    public class CameraSpring : MonoBehaviour
    {
        [Min(0.1f)]
        [SerializeField] private float _halfLife = 0.075f;
        [Space]
        [SerializeField] private float _frequency = 18f;
        [Space]
        [SerializeField] private float _angularDisplacement = 2f;
        [SerializeField] private float _linearDisplacement = 0.05f;

        private Vector3 _springPosition;
        private Vector3 _springVelocity;

        /// <summary>Seed the spring at the current transform position with zero velocity.</summary>
        public void Initialize()
        {
            _springPosition = transform.position;
            _springVelocity = Vector3.zero;
        }

        /// <summary>Advance the spring and apply the resulting positional lag and pitch kick to this transform.</summary>
        public void UpdateSpring(float deltaTime, Vector3 up)
        {
            transform.localPosition = Vector3.zero;

            SpringMath.Spring(ref _springPosition, ref _springVelocity, transform.position, _halfLife, _frequency, deltaTime);

            Vector3 relativeSpringPosition = _springPosition - transform.position;
            float springHeight = Vector3.Dot(relativeSpringPosition, up);

            transform.localEulerAngles = new Vector3(-springHeight * _angularDisplacement, 0f, 0f);
            transform.position += relativeSpringPosition * _linearDisplacement;
        }

        private void OnDrawGizmos()
        {
            Gizmos.color = Color.green;
            Gizmos.DrawLine(transform.position, _springPosition);
            Gizmos.DrawSphere(_springPosition, 0.1f);
        }
    }
}
