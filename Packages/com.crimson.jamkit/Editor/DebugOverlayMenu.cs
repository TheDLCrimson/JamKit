using UnityEditor;
using UnityEngine;

namespace JamKit.Editor
{
    /// <summary>
    /// Editor convenience for dropping a <see cref="DebugOverlay"/> into the open scene. The overlay type only
    /// exists under <c>UNITY_EDITOR || DEVELOPMENT_BUILD</c>; this menu lives in the always-editor Editor asmdef,
    /// where <c>UNITY_EDITOR</c> is defined, so the reference resolves.
    /// </summary>
    public static class DebugOverlayMenu
    {
        [MenuItem("JamKit/Add Debug Overlay")]
        private static void AddDebugOverlay()
        {
            GameObject go = new GameObject(nameof(DebugOverlay));
            go.AddComponent<DebugOverlay>();
            Undo.RegisterCreatedObjectUndo(go, "Add Debug Overlay");
            Selection.activeGameObject = go;
        }
    }
}
