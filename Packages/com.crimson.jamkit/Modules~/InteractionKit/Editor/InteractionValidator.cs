using System.Collections.Generic;
using System.Text;
using UnityEditor;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace JamKit.InteractionKit.Editor
{
    /// <summary>
    /// Scene-wiring checks for the open scene. Every check here corresponds to a defect that cost
    /// real time on a shipped project, not to a hypothetical mistake.
    /// </summary>
    /// <remarks>
    /// These ship with the module rather than inside JamKit's <c>ProjectValidator</c>, which is
    /// frozen at v1.0. That turns out to be the better home anyway: copying the module into a
    /// project brings its checks with it, and removing the module removes them, so there is never a
    /// validator rule referring to components the project does not have.
    /// </remarks>
    public static class InteractionValidator
    {
        private const string LogPrefix = "[InteractionKit Validate] ";

        [MenuItem("JamKit/Validate Interaction Wiring")]
        private static void ValidateMenuItem()
        {
            List<string> errors = new List<string>();
            List<string> warnings = new List<string>();
            Validate(errors, warnings);

            var sb = new StringBuilder();
            sb.Append(LogPrefix)
              .Append(errors.Count == 0 ? "PASS" : "FAIL")
              .Append(" - ").Append(errors.Count).Append(" error(s), ")
              .Append(warnings.Count).Append(" warning(s) in scene '")
              .Append(SceneManager.GetActiveScene().name).Append("'.");

            for (int i = 0; i < errors.Count; i++)
                sb.Append("\n  ERROR: ").Append(errors[i]);
            for (int i = 0; i < warnings.Count; i++)
                sb.Append("\n  WARN:  ").Append(warnings[i]);

            if (errors.Count > 0)
                Debug.LogError(sb.ToString());
            else if (warnings.Count > 0)
                Debug.LogWarning(sb.ToString());
            else
                Debug.Log(sb.ToString());
        }

        /// <summary>
        /// Runs every wiring check against the open scene, appending to the supplied lists.
        /// Exposed so a project's own validator or a test can call it directly.
        /// </summary>
        public static void Validate(List<string> errors, List<string> warnings)
        {
            SignalNode[] nodes = Object.FindObjectsByType<SignalNode>(
                FindObjectsInactive.Include, FindObjectsSortMode.None);

            CheckEmptyOutputs(nodes, warnings);
            CheckGates(errors, warnings);
            CheckSockets(errors, warnings);
            CheckTriggerZones(errors, warnings);
            CheckBlockedBy(warnings);
            CheckFeedback(warnings);
            CheckInteractorPresence(nodes, errors);
            CheckSwitchColliders(errors);
        }

        // An empty output row is skipped silently at runtime, which is indistinguishable from a
        // mechanic that simply does not work.
        private static void CheckEmptyOutputs(SignalNode[] nodes, List<string> warnings)
        {
            for (int i = 0; i < nodes.Length; i++)
            {
                SignalNode node = nodes[i];
                for (int r = 0; r < node.OutputCount; r++)
                {
                    if (node.OutputTargetAt(r) == null)
                        warnings.Add("Empty output row " + r + " on '" + Path(node.transform) + "'.");
                }
            }
        }

        // THE check. Every objective room in GMTK 2026 shipped with a Gate whose inputs were null.
        // Gate.Update skips nulls, so the threshold could never be met, nothing logged, and four
        // rooms were unfinishable for two days.
        private static void CheckGates(List<string> errors, List<string> warnings)
        {
            Gate[] gates = Object.FindObjectsByType<Gate>(FindObjectsInactive.Include, FindObjectsSortMode.None);
            for (int i = 0; i < gates.Length; i++)
            {
                Gate gate = gates[i];
                string where = "'" + Path(gate.transform) + "'";

                if (gate.InputCount == 0)
                {
                    errors.Add("Gate " + where + " has no inputs, so it can never turn on.");
                    continue;
                }

                int empty = 0;
                for (int r = 0; r < gate.InputCount; r++)
                    if (gate.InputAt(r) == null)
                        empty++;

                if (empty > 0)
                    errors.Add("Gate " + where + " has " + empty + " of " + gate.InputCount +
                               " input rows empty. It can never reach its threshold, and nothing logs at runtime.");

                if (gate.Threshold > gate.InputCount)
                    errors.Add("Gate " + where + " has threshold " + gate.Threshold + " but only " +
                               gate.InputCount + " inputs, so it can never be satisfied.");

                if (gate.OutputCount == 0)
                    warnings.Add("Gate " + where + " drives nothing. Satisfying it will have no visible effect.");
            }
        }

        private static void CheckSockets(List<string> errors, List<string> warnings)
        {
            Socket[] sockets = Object.FindObjectsByType<Socket>(FindObjectsInactive.Include, FindObjectsSortMode.None);
            Carryable[] props = Object.FindObjectsByType<Carryable>(FindObjectsInactive.Include, FindObjectsSortMode.None);

            for (int i = 0; i < sockets.Length; i++)
            {
                Socket socket = sockets[i];
                string where = "'" + Path(socket.transform) + "'";

                Collider col = socket.GetComponent<Collider>();
                if (col == null)
                    errors.Add("Socket " + where + " has no Collider, so nothing can ever be inserted.");
                else if (!col.isTrigger)
                    errors.Add("Socket " + where + " has a Collider that is not a trigger, so nothing can ever be inserted.");

                if (string.IsNullOrEmpty(socket.Accepts))
                {
                    warnings.Add("Socket " + where + " accepts an empty id, so it matches any Carryable with no id set.");
                    continue;
                }

                bool matched = false;
                for (int p = 0; p < props.Length; p++)
                {
                    if (props[p].Id == socket.Accepts)
                    {
                        matched = true;
                        break;
                    }
                }

                // A typo between socket and prop is invisible at edit time and produces a socket
                // that silently never accepts anything.
                if (!matched)
                    errors.Add("Socket " + where + " accepts \"" + socket.Accepts +
                               "\" but no Carryable in this scene has that id.");
            }
        }

        private static void CheckTriggerZones(List<string> errors, List<string> warnings)
        {
            TriggerZone[] zones = Object.FindObjectsByType<TriggerZone>(FindObjectsInactive.Include, FindObjectsSortMode.None);
            for (int i = 0; i < zones.Length; i++)
            {
                TriggerZone zone = zones[i];
                string where = "'" + Path(zone.transform) + "'";

                if (zone.Target == null)
                    warnings.Add("TriggerZone " + where + " drives nothing.");

                Collider col = zone.GetComponent<Collider>();
                if (col != null && !col.isTrigger)
                    errors.Add("TriggerZone " + where + " has a Collider that is not a trigger, so it will never fire.");
            }

            if (zones.Length == 0)
                return;

            // Unity raises OnTriggerEnter only when both parties have a Collider and at least one
            // has a Rigidbody. A player rig built as a bare transform walks straight through every
            // trigger volume in the scene and nothing fires, with no error anywhere.
            Interactor[] rigs = Object.FindObjectsByType<Interactor>(FindObjectsInactive.Include, FindObjectsSortMode.None);
            for (int i = 0; i < rigs.Length; i++)
            {
                GameObject rig = rigs[i].gameObject;
                bool hasCollider = rig.GetComponentInChildren<Collider>(true) != null
                                   || rig.GetComponentInParent<Collider>() != null;
                bool hasBody = rig.GetComponentInChildren<Rigidbody>(true) != null
                               || rig.GetComponentInParent<Rigidbody>() != null
                               || rig.GetComponentInChildren<CharacterController>(true) != null;

                if (!hasCollider || !hasBody)
                    errors.Add("This scene has " + zones.Length + " TriggerZone(s), but the player rig '" +
                               Path(rig.transform) + "' has " +
                               (!hasCollider ? "no Collider" : "no Rigidbody or CharacterController") +
                               ", so it can never trigger any of them.");
            }
        }

        private static void CheckBlockedBy(List<string> warnings)
        {
            BlockedBy[] blocks = Object.FindObjectsByType<BlockedBy>(FindObjectsInactive.Include, FindObjectsSortMode.None);
            for (int i = 0; i < blocks.Length; i++)
            {
                if (blocks[i].Source == null)
                    warnings.Add("BlockedBy on '" + Path(blocks[i].transform) +
                                 "' has no source, so it never blocks anything.");
            }
        }

        // A scene full of switches and no Interactor is a scene where nothing can be used. This is
        // the single most likely reason for "I pressed E and nothing happened".
        // SignalVisual and SignalSound observe a node instead of being one, so they resolve a source
        // from their own GameObject. On an object that carries no node they silently do nothing.
        private static void CheckFeedback(List<string> warnings)
        {
            SignalVisual[] visuals = Object.FindObjectsByType<SignalVisual>(FindObjectsInactive.Include, FindObjectsSortMode.None);
            for (int i = 0; i < visuals.Length; i++)
            {
                if (visuals[i].Source == null && visuals[i].GetComponent<SignalNode>() == null)
                    warnings.Add("SignalVisual on '" + Path(visuals[i].transform) +
                                 "' has no source node and none on its GameObject, so it never changes colour.");
            }

            SignalSound[] sounds = Object.FindObjectsByType<SignalSound>(FindObjectsInactive.Include, FindObjectsSortMode.None);
            for (int i = 0; i < sounds.Length; i++)
            {
                if (sounds[i].Node == null && sounds[i].GetComponent<SignalNode>() == null)
                    warnings.Add("SignalSound on '" + Path(sounds[i].transform) +
                                 "' has no source node and none on its GameObject, so it never plays.");
            }
        }

        private static void CheckInteractorPresence(SignalNode[] nodes, List<string> errors)
        {
            bool anyInteractable = false;
            for (int i = 0; i < nodes.Length; i++)
            {
                if (nodes[i] is IInteractable)
                {
                    anyInteractable = true;
                    break;
                }
            }

            if (!anyInteractable)
                return;

            Interactor[] interactors = Object.FindObjectsByType<Interactor>(
                FindObjectsInactive.Include, FindObjectsSortMode.None);

            if (interactors.Length == 0)
                errors.Add("This scene has interactable nodes but no Interactor. Nothing can be used. " +
                           "Add one to the player rig, or load the player from another scene if that is intended.");
        }

        private static void CheckSwitchColliders(List<string> errors)
        {
            Switch[] switches = Object.FindObjectsByType<Switch>(FindObjectsInactive.Include, FindObjectsSortMode.None);
            for (int i = 0; i < switches.Length; i++)
            {
                if (switches[i].GetComponent<Collider>() == null)
                    errors.Add("Switch '" + Path(switches[i].transform) + "' has no Collider.");
            }
        }

        private static string Path(Transform t)
        {
            string path = t.name;
            Transform parent = t.parent;
            while (parent != null)
            {
                path = parent.name + "/" + path;
                parent = parent.parent;
            }
            return path;
        }
    }
}
