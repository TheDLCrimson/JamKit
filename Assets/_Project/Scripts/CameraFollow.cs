using JamKit;
using UnityEngine;

// CameraFollow: minimal sample camera follow - fixed offset, MathUtil.Decay smoothing at a sample response
// rate, no lookahead/deadzone/framing logic. Deliberately simple so the Lockdown sample keeps the player on
// screen without prescribing a feel; feel-tuning or replacing it is human-owned (core mechanic feel is
// never delegated) - this script intentionally does none. Plain Assembly-CSharp, no namespace.
public class CameraFollow : MonoBehaviour
{
    [Header("Wiring")]
    [SerializeField] private Transform _target;

    [Header("Tuning (sample defaults - not feel-tuned)")]
    [SerializeField] private Vector3 _offset = new Vector3(0f, 1f, -10f);
    [SerializeField] private float _response = 5f;

    private void LateUpdate()
    {
        if (_target == null)
        {
            return;
        }

        Vector3 desired = _target.position + _offset;
        transform.position = MathUtil.Decay(transform.position, desired, _response, Time.deltaTime);
    }
}
