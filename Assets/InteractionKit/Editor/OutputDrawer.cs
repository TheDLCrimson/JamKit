using UnityEditor;
using UnityEngine;

namespace JamKit.InteractionKit.Editor
{
    /// <summary>
    /// Draws an <see cref="Output"/> row as "target | mode" on one line, and accepts a GameObject
    /// dropped from the hierarchy by resolving the node on it.
    /// </summary>
    /// <remarks>
    /// This drawer exists to kill one specific authoring trap. A default object field typed as
    /// <see cref="SignalNode"/> rejects a GameObject dragged from the hierarchy, so the author had
    /// to expand the target in the inspector and drag the component out of its header instead.
    /// Dragging the GameObject did nothing, or - worse, on an object carrying more than one node -
    /// wired a different node than the one intended. The v1 README documented the workaround, which
    /// is the tell that it was a design problem rather than a knowledge problem.
    /// </remarks>
    [CustomPropertyDrawer(typeof(Output))]
    public sealed class OutputDrawer : PropertyDrawer
    {
        private const float ModeWidth = 74f;
        private const float Gap = 4f;

        public override void OnGUI(Rect position, SerializedProperty property, GUIContent label)
        {
            SerializedProperty target = property.FindPropertyRelative("Target");
            SerializedProperty mode = property.FindPropertyRelative("Mode");

            EditorGUI.BeginProperty(position, label, property);

            Rect targetRect = new Rect(position.x, position.y, position.width - ModeWidth - Gap, position.height);
            Rect modeRect = new Rect(position.xMax - ModeWidth, position.y, ModeWidth, position.height);

            AcceptGameObjectDrop(targetRect, target);

            EditorGUI.PropertyField(targetRect, target, GUIContent.none);
            EditorGUI.PropertyField(modeRect, mode, GUIContent.none);

            EditorGUI.EndProperty();
        }

        public override float GetPropertyHeight(SerializedProperty property, GUIContent label) =>
            EditorGUIUtility.singleLineHeight;

        // Intercepts the drag before the object field sees it, so a GameObject carrying a node is
        // resolved to that node rather than being rejected as the wrong type.
        private static void AcceptGameObjectDrop(Rect rect, SerializedProperty target)
        {
            Event e = Event.current;
            if (e.type != EventType.DragUpdated && e.type != EventType.DragPerform)
                return;
            if (!rect.Contains(e.mousePosition))
                return;

            SignalNode resolved = null;
            for (int i = 0; i < DragAndDrop.objectReferences.Length; i++)
            {
                Object dragged = DragAndDrop.objectReferences[i];
                if (dragged is SignalNode node)
                {
                    resolved = node;
                    break;
                }
                if (dragged is GameObject go)
                {
                    SignalNode found = go.GetComponent<SignalNode>();
                    if (found != null)
                    {
                        resolved = found;
                        break;
                    }
                }
            }

            if (resolved == null)
                return;

            DragAndDrop.visualMode = DragAndDropVisualMode.Link;

            if (e.type == EventType.DragPerform)
            {
                DragAndDrop.AcceptDrag();
                target.objectReferenceValue = resolved;
                e.Use();
            }
        }
    }
}
