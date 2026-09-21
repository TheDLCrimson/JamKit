using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

namespace JamKit.InteractionKit.Editor
{
    /// <summary>
    /// Door inspector with a Preview Open toggle that poses the door in the scene view, so the open
    /// offset and swing are authored by eye instead of by typing numbers and entering play mode.
    /// </summary>
    /// <remarks>
    /// The preview always restores the authored pose when switched off or when the inspector closes.
    /// That matters more than it sounds: the authored transform IS the closed pose by contract, so a
    /// preview that left the door posed would silently rewrite the thing the component measures from.
    /// </remarks>
    [CustomEditor(typeof(Door))]
    public sealed class DoorEditor : SignalNodeEditor
    {
        private bool _previewing;
        private Vector3 _savedLocalPosition;
        private Quaternion _savedLocalRotation;
        private Transform _savedTransform;

        protected override void DrawHeaderExtras(SignalNode node)
        {
            // Edit-mode only: in play mode the door is really moving, and posing it behind the
            // motion would fight Update. The Toggle button beside this covers play mode.
            using (new EditorGUI.DisabledScope(Application.isPlaying))
            {
                bool next = GUILayout.Toggle(_previewing, "Preview Open", EditorStyles.miniButton, GUILayout.Width(100f));
                if (next != _previewing)
                    SetPreview((Door)node, next);
            }
        }

        private void OnDisable() => SetPreview(target as Door, false);

        private void SetPreview(Door door, bool on)
        {
            if (door == null)
                return;

            if (on)
            {
                _savedTransform = door.Moved;
                if (_savedTransform == null)
                    return;

                _savedLocalPosition = _savedTransform.localPosition;
                _savedLocalRotation = _savedTransform.localRotation;
                _previewing = true;
                door.PreviewAt(1f, _savedLocalPosition, _savedLocalRotation);
            }
            else
            {
                if (_previewing && _savedTransform != null)
                {
                    // Restore rather than PreviewAt(0): floating point round-tripping through
                    // SmoothStep would leave the authored closed pose a hair off its typed values.
                    _savedTransform.localPosition = _savedLocalPosition;
                    _savedTransform.localRotation = _savedLocalRotation;
                }
                _previewing = false;
                _savedTransform = null;
            }

            SceneView.RepaintAll();
        }

        protected override void CollectExtraIssues(SignalNode node, List<string> issues)
        {
            var door = (Door)node;
            if (door.OpenLocalOffset == Vector3.zero && door.OpenLocalEuler == Vector3.zero)
                issues.Add("Open Offset and Open Rotation are both zero, so this door will not visibly move.");
        }
    }
}
