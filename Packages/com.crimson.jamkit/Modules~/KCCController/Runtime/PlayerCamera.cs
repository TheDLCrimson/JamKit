using UnityEngine;

namespace JamKit.KCC
{
    /// <summary>
    /// A minimal first-person look controller: accumulates look input into euler angles with a
    /// pitch clamp, and follows a target transform's position.
    /// </summary>
    /// <remarks>
    /// Driven by <see cref="Player"/>. JamKit's CameraSpring and CameraLean are designed to sit
    /// under this transform and add procedural motion on top; see INSTALL.md.
    /// </remarks>
    public class PlayerCamera : MonoBehaviour
    {
        [SerializeField] private float _sensitivity = 0.1f;
        [SerializeField] private float _clampAngle = 90f;
        [SerializeField] private Vector3 _eulerAngles;

        /// <summary>
        /// Snaps the camera to the target. Call once, before the first update.
        /// </summary>
        public void Initialize(Transform target)
        {
            transform.position = target.position;
            transform.eulerAngles = _eulerAngles = target.eulerAngles;
        }

        /// <summary>
        /// Applies one frame of look input. Call from Update.
        /// </summary>
        public void UpdateRotation(CameraInput input)
        {
            _eulerAngles += new Vector3(-input.Look.y, input.Look.x) * _sensitivity;
            _eulerAngles.x = Mathf.Clamp(_eulerAngles.x, -_clampAngle, _clampAngle);
            transform.eulerAngles = _eulerAngles;
        }

        /// <summary>
        /// Follows the target's position. Call from LateUpdate, after the character has moved.
        /// </summary>
        public void UpdatePosition(Transform target)
        {
            transform.position = target.position;
        }
    }
}
