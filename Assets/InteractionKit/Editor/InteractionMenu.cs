using UnityEditor;
using UnityEngine;

namespace JamKit.InteractionKit.Editor
{
    /// <summary>
    /// GameObject creation menu and the global wiring-gizmo toggle.
    /// </summary>
    /// <remarks>
    /// Every entry here produces an object that already works: correct primitive, correct collider
    /// setup, sensible physics, and a label. The point is that the button-opens-door case should
    /// cost two menu picks and one wire, with nothing typed - if an author has to fix a default
    /// before the thing functions, the default was wrong.
    /// </remarks>
    public static class InteractionMenu
    {
        private const string ShowWiringPref = "JamKit.InteractionKit.ShowWiring";
        private const string ShowWiringMenu = "JamKit/Interaction/Show Wiring";

        [InitializeOnLoadMethod]
        private static void LoadPrefs()
        {
            InteractionGizmos.ShowWiring = EditorPrefs.GetBool(ShowWiringPref, false);
            EditorApplication.delayCall += () =>
                Menu.SetChecked(ShowWiringMenu, InteractionGizmos.ShowWiring);
        }

        [MenuItem(ShowWiringMenu, priority = 20)]
        private static void ToggleShowWiring()
        {
            InteractionGizmos.ShowWiring = !InteractionGizmos.ShowWiring;
            EditorPrefs.SetBool(ShowWiringPref, InteractionGizmos.ShowWiring);
            Menu.SetChecked(ShowWiringMenu, InteractionGizmos.ShowWiring);
            SceneView.RepaintAll();
        }

        [MenuItem("GameObject/JamKit/Interaction/Switch", false, 10)]
        private static void CreateSwitch(MenuCommand command)
        {
            GameObject go = CreatePrimitive(command, PrimitiveType.Cube, "Switch", new Vector3(0.3f, 0.3f, 0.12f));
            var node = go.AddComponent<Switch>();
            node.Configure("Use", SwitchMode.Toggle);
            go.AddComponent<SignalVisual>();
            Select(go);
        }

        [MenuItem("GameObject/JamKit/Interaction/Door", false, 11)]
        private static void CreateDoor(MenuCommand command)
        {
            GameObject go = CreatePrimitive(command, PrimitiveType.Cube, "Door", new Vector3(1.2f, 2.2f, 0.12f));
            var door = go.AddComponent<Door>();
            door.Configure(new Vector3(0f, 2.2f, 0f), 0.6f);
            Select(go);
        }

        [MenuItem("GameObject/JamKit/Interaction/Gate", false, 12)]
        private static void CreateGate(MenuCommand command)
        {
            GameObject go = CreateEmpty(command, "Gate");
            go.AddComponent<Gate>();
            Select(go);
        }

        [MenuItem("GameObject/JamKit/Interaction/Socket", false, 13)]
        private static void CreateSocket(MenuCommand command)
        {
            GameObject go = CreatePrimitive(command, PrimitiveType.Cube, "Socket", new Vector3(0.4f, 0.25f, 0.4f));
            Collider col = go.GetComponent<Collider>();
            if (col != null)
                col.isTrigger = true;
            go.AddComponent<Socket>();
            go.AddComponent<SignalVisual>();
            Select(go);
        }

        [MenuItem("GameObject/JamKit/Interaction/Carryable Prop", false, 14)]
        private static void CreateCarryable(MenuCommand command)
        {
            GameObject go = CreatePrimitive(command, PrimitiveType.Cube, "Carryable Prop", Vector3.one * 0.3f);
            var body = go.AddComponent<Rigidbody>();
            body.mass = 2f;
            go.AddComponent<Carryable>();
            Select(go);
        }

        [MenuItem("GameObject/JamKit/Interaction/Trigger Zone", false, 15)]
        private static void CreateTriggerZone(MenuCommand command)
        {
            GameObject go = CreateEmpty(command, "Trigger Zone");
            var col = go.AddComponent<BoxCollider>();
            col.isTrigger = true;
            col.size = new Vector3(2f, 2f, 2f);
            go.AddComponent<TriggerZone>();
            Select(go);
        }

        [MenuItem("GameObject/JamKit/Interaction/Signal Relay", false, 16)]
        private static void CreateRelay(MenuCommand command)
        {
            GameObject go = CreateEmpty(command, "Signal Relay");
            go.AddComponent<SignalRelay>();
            Select(go);
        }

        private static GameObject CreatePrimitive(MenuCommand command, PrimitiveType type, string name, Vector3 scale)
        {
            GameObject go = GameObject.CreatePrimitive(type);
            go.name = name;
            go.transform.localScale = scale;
            Place(go, command);
            return go;
        }

        private static GameObject CreateEmpty(MenuCommand command, string name)
        {
            var go = new GameObject(name);
            Place(go, command);
            return go;
        }

        // GameObjectUtility puts the new object under whatever was right-clicked and keeps it in the
        // correct scene, which matters in a multi-scene setup where the active scene is not the one
        // being authored.
        private static void Place(GameObject go, MenuCommand command)
        {
            GameObjectUtility.SetParentAndAlign(go, command.context as GameObject);
            Undo.RegisterCreatedObjectUndo(go, "Create " + go.name);
        }

        private static void Select(GameObject go) => Selection.activeGameObject = go;
    }
}
