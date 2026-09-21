using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

namespace JamKit.InteractionKit.Editor
{
    /// <summary>
    /// Gate inspector: a live tick list of which inputs are satisfied, plus the empty-input warning.
    /// </summary>
    /// <remarks>
    /// The tick list is the direct answer to the defect that cost GMTK 2026 four rooms for two days.
    /// Every objective room shipped with a Gate whose inputs were null, <c>Gate.Update</c> skips
    /// nulls silently, so no threshold could ever be met and the rooms were unfinishable with no
    /// error anywhere. Showing the count and naming each input makes that state impossible to miss.
    /// </remarks>
    [CustomEditor(typeof(Gate))]
    [CanEditMultipleObjects]
    public sealed class GateEditor : SignalNodeEditor
    {
        public override void OnInspectorGUI()
        {
            base.OnInspectorGUI();

            if (serializedObject.isEditingMultipleObjects)
                return;

            var gate = (Gate)target;
            if (gate.InputCount == 0)
                return;

            EditorGUILayout.Space(4f);
            EditorGUILayout.LabelField(
                "Inputs satisfied: " + gate.SatisfiedCount + " / " + gate.InputCount +
                "  (needs " + Mathf.Max(1, gate.Threshold) + (gate.Invert ? ", inverted)" : ")"),
                EditorStyles.boldLabel);

            using (new EditorGUI.IndentLevelScope())
            {
                for (int i = 0; i < gate.InputCount; i++)
                {
                    SignalNode input = gate.InputAt(i);
                    if (input == null)
                    {
                        EditorGUILayout.LabelField("✗  (empty row " + i + ")");
                        continue;
                    }
                    EditorGUILayout.LabelField((input.State ? "✔  " : "○  ") + input.name);
                }
            }
        }

        protected override void CollectExtraIssues(SignalNode node, List<string> issues)
        {
            var gate = (Gate)node;

            int empty = 0;
            for (int i = 0; i < gate.InputCount; i++)
                if (gate.InputAt(i) == null)
                    empty++;

            if (empty > 0)
                issues.Add(empty + " of " + gate.InputCount + " input rows are empty. Gate skips nulls " +
                           "silently, so this gate can never reach its threshold and anything behind it " +
                           "is unreachable.");

            if (gate.InputCount == 0)
                issues.Add("This gate has no inputs, so it can never turn on.");

            if (gate.Threshold > gate.InputCount && gate.InputCount > 0)
                issues.Add("Threshold (" + gate.Threshold + ") is higher than the number of inputs (" +
                           gate.InputCount + "), so this gate can never be satisfied.");
        }
    }
}
