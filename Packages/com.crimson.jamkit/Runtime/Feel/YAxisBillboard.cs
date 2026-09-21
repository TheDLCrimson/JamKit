using UnityEngine;

namespace JamKit
{
    /// <summary>
    /// Rotates this object to face the main camera on the XZ plane only (stays upright). Dependency-free.
    /// </summary>
    /// <remarks>Extracted verbatim (namespaced) from CarGoesAround's <c>YAxisBillboard</c>.</remarks>
    public class YAxisBillboard : MonoBehaviour
    {
        private void LateUpdate()
        {
            if (Camera.main == null) return;

            Vector3 cameraPos = Camera.main.transform.position;

            Vector3 lookDirection = new Vector3(
                cameraPos.x - transform.position.x,
                0,
                cameraPos.z - transform.position.z
            );

            if (lookDirection.sqrMagnitude > 0.001f)
            {
                transform.rotation = Quaternion.LookRotation(lookDirection);
            }
        }
    }
}
