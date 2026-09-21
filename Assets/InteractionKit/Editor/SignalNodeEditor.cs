using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

namespace JamKit.InteractionKit.Editor
{
    /// <summary>
    /// Base inspector for every node: a live state pill, a Toggle button that works in and out of
    /// play mode, the "+ Wire to" picker that replaces dragging a component out of an inspector
    /// header, and an inline validation block.
    /// </summary>
    [CustomEditor(typeof(SignalNode), true)]
    [CanEditMultipleObjects]
    public class SignalNodeEditor : UnityEditor.Editor
    {
        private static readonly Color OnColor = new Color(0.25f, 0.8f, 0.4f);
        private static readonly Color OffColor = new Color(0.45f, 0.45f, 0.5f);

        private readonly List<string> _issues = new List<string>();
        private int _pickerControlId = -1;

        public override void OnInspectorGUI()
        {
            var node = (SignalNode)target;

            DrawStatePill(node);
            DrawDefaultInspector();
            DrawWireButton();
            HandlePickerResult();

            _issues.Clear();
            CollectIssues(node, _issues);
            for (int i = 0; i < _issues.Count; i++)
                EditorGUILayout.HelpBox(_issues[i], MessageType.Warning);

            // The pill shows a stale state otherwise, which is worse than showing none: it reads as
            // a broken link when the wiring is fine.
            if (Application.isPlaying)
                Repaint();
        }

        /// <summary>Override to add leaf-specific warnings to the inline validation block.</summary>
        protected virtual void CollectExtraIssues(SignalNode node, List<string> issues) { }

        /// <summary>Override to add a leaf-specific control to the header row, beside the Toggle button.</summary>
        protected virtual void DrawHeaderExtras(SignalNode node) { }

        private void DrawStatePill(SignalNode node)
        {
            using (new EditorGUILayout.HorizontalScope())
            {
                Color previous = GUI.color;
                GUI.color = node.State ? OnColor : OffColor;
                GUILayout.Label(node.State ? "● ON" : "○ OFF", EditorStyles.boldLabel, GUILayout.Width(60f));
                GUI.color = previous;

                using (new EditorGUI.DisabledScope(serializedObject.isEditingMultipleObjects))
                {
                    // Works outside play mode too: SetState pushes to outputs immediately, so a whole
                    // wiring chain can be checked without entering play mode at all.
                    if (GUILayout.Button("Toggle", EditorStyles.miniButton, GUILayout.Width(70f)))
                    {
                        Undo.RecordObject(node, "Toggle Signal Node");
                        node.Toggle();
                        EditorUtility.SetDirty(node);
                    }

                    // Leaf-specific controls sit here, in the header row, rather than below the
                    // fields and the validation block where they fall under the fold on any node
                    // with more than a couple of properties.
                    DrawHeaderExtras(node);
                }

                GUILayout.FlexibleSpace();
            }
        }

        private void DrawWireButton()
        {
            if (serializedObject.isEditingMultipleObjects)
                return;
            if (serializedObject.FindProperty("_outputs") == null)
                return;

            if (!GUILayout.Button("+ Wire to…"))
                return;

            // An object picker rather than a drag: the author clicks the target in a searchable list
            // instead of finding it in the hierarchy and dragging precisely onto a list row. The
            // picker takes a GameObject and the node is resolved from it, because "which component
            // did you mean" is exactly the question the author should not have to answer.
            _pickerControlId = GUIUtility.GetControlID(FocusType.Passive);
            EditorGUIUtility.ShowObjectPicker<GameObject>(null, true, string.Empty, _pickerControlId);
        }

        // ShowObjectPicker reports through a command event on a later inspector repaint, so this has
        // to be polled from inside OnInspectorGUI rather than handled where the picker was opened.
        private void HandlePickerResult()
        {
            if (_pickerControlId == -1)
                return;

            Event e = Event.current;
            if (e == null || e.type != EventType.ExecuteCommand)
                return;
            if (e.commandName != "ObjectSelectorClosed")
                return;
            if (EditorGUIUtility.GetObjectPickerControlID() != _pickerControlId)
                return;

            var picked = EditorGUIUtility.GetObjectPickerObject() as GameObject;
            _pickerControlId = -1;
            e.Use();

            if (picked == null)
                return;

            SignalNode node = picked.GetComponent<SignalNode>();
            if (node == null)
            {
                Debug.LogWarning("[InteractionKit] " + picked.name + " has no SignalNode on it, so there is nothing to wire to.", picked);
                return;
            }

            if (node == (SignalNode)target)
            {
                Debug.LogWarning("[InteractionKit] A node cannot be wired to itself.", picked);
                return;
            }

            SerializedProperty outputs = serializedObject.FindProperty("_outputs");
            int index = outputs.arraySize;
            outputs.InsertArrayElementAtIndex(index);
            SerializedProperty row = outputs.GetArrayElementAtIndex(index);
            row.FindPropertyRelative("Target").objectReferenceValue = node;
            row.FindPropertyRelative("Mode").enumValueIndex = (int)OutputMode.Follow;
            serializedObject.ApplyModifiedProperties();
        }

        private void CollectIssues(SignalNode node, List<string> issues)
        {
            for (int i = 0; i < node.OutputCount; i++)
            {
                if (node.OutputTargetAt(i) == null)
                    issues.Add("Output row " + i + " is empty. It is skipped silently at runtime, which looks exactly like a mechanic that does not work.");
            }

            CollectExtraIssues(node, issues);
        }
    }
}
