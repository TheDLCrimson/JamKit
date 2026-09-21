using System.Collections;
using System.Collections.Generic;
using System.Text;
using UnityEngine;

namespace JamKit.InteractionKit.Demo
{
    /// <summary>
    /// Builds every recipe the kit supports in code, drives it, and logs one PASS/FAIL line per
    /// recipe. Put it on an empty GameObject and press Play.
    /// </summary>
    /// <remarks>
    /// This is the showcase's headless twin. The scene proves the recipes are authorable by hand;
    /// this proves they still behave after a change, without anyone having to walk the scene. It
    /// runs in play mode because doors move over real frames and sockets need physics triggers.
    /// </remarks>
    [AddComponentMenu("")]
    public sealed class InteractionKitSelfTest : MonoBehaviour
    {
        [Tooltip("Run automatically on Start. Turn off to drive it from the context menu instead.")]
        [SerializeField] private bool _runOnStart = true;

        private readonly List<string> _results = new List<string>();
        private readonly List<GameObject> _spawned = new List<GameObject>();
        private int _passed;
        private int _failed;

        private void Start()
        {
            if (_runOnStart)
                StartCoroutine(RunAll());
        }

        [ContextMenu("Run Self Test")]
        private void RunFromMenu() => StartCoroutine(RunAll());

        private IEnumerator RunAll()
        {
            _results.Clear();
            _spawned.Clear();
            _passed = 0;
            _failed = 0;

            Recipe1ButtonOpensDoor();
            Recipe2ThreeBreakersAndGate();
            Recipe3AnyGate();
            Recipe4InvertedGate();
            Recipe5MomentaryPulseLatch();
            Recipe6OnceLatch();
            Recipe7BlockedByPower();
            Recipe8BlockedByManyTargets();
            Recipe9RelayEscapeHatch();
            Recipe10ChainedNodes();
            Recipe11SignalVisual();
            yield return Recipe12SocketSeatsAProp();

            Report();
            Cleanup();
        }

        // --- recipes ------------------------------------------------------------------------

        private void Recipe1ButtonOpensDoor()
        {
            Switch button = Make<Switch>("button");
            Door door = Make<Door>("door");
            button.Configure("Open Door", SwitchMode.Toggle);
            door.Configure(new Vector3(0f, 3f, 0f), 0.4f);
            button.AddOutput(door, OutputMode.Follow);

            button.Interact(this);
            Check("1. Button opens door", door.State);

            button.Interact(this);
            Check("1b. Button closes it again", !door.State);
        }

        private void Recipe2ThreeBreakersAndGate()
        {
            Switch a = Make<Switch>("breakerA");
            Switch b = Make<Switch>("breakerB");
            Switch c = Make<Switch>("breakerC");
            Gate gate = Make<Gate>("allThree");
            Door door = Make<Door>("vault");

            gate.Configure(3, false, a, b, c);
            gate.AddOutput(door, OutputMode.Follow);

            a.Interact(this);
            b.Interact(this);
            gate.Evaluate();
            Check("2. Two of three breakers does NOT open", !door.State);

            c.Interact(this);
            gate.Evaluate();
            Check("2b. All three breakers opens", door.State);
        }

        private void Recipe3AnyGate()
        {
            Switch a = Make<Switch>("anyA");
            Switch b = Make<Switch>("anyB");
            Gate gate = Make<Gate>("anyGate");
            gate.Configure(1, false, a, b);

            b.Interact(this);
            gate.Evaluate();
            Check("3. Threshold 1 behaves as ANY", gate.State);
        }

        private void Recipe4InvertedGate()
        {
            Switch a = Make<Switch>("invA");
            Gate gate = Make<Gate>("notGate");
            gate.Configure(1, true, a);

            gate.Evaluate();
            Check("4. Inverted gate is on while unsatisfied", gate.State);

            a.Interact(this);
            gate.Evaluate();
            Check("4b. Inverted gate turns off once satisfied", !gate.State);
        }

        private void Recipe5MomentaryPulseLatch()
        {
            Switch momentary = Make<Switch>("momentary");
            SignalRelay latch = Make<SignalRelay>("latch");
            momentary.Configure("Press", SwitchMode.Momentary, 0.05f);
            momentary.AddOutput(latch, OutputMode.Pulse);

            momentary.Interact(this);
            Check("5. Momentary + Pulse latches the target on", latch.State);
        }

        private void Recipe6OnceLatch()
        {
            Switch once = Make<Switch>("once");
            once.Configure("Commit", SwitchMode.Once);

            once.Interact(this);
            once.Interact(this);
            Check("6. Once stays on (point of no return)", once.State);
        }

        private void Recipe7BlockedByPower()
        {
            SignalRelay generator = Make<SignalRelay>("generator");
            Switch terminal = Make<Switch>("terminal");
            terminal.gameObject.AddComponent<BlockedBy>().Configure(generator, terminal, "Needs power", false);

            terminal.Interact(this);
            Check("7. Unpowered terminal refuses", !terminal.State);

            generator.SetState(true);
            terminal.Interact(this);
            Check("7b. Powered terminal accepts", terminal.State);
        }

        private void Recipe8BlockedByManyTargets()
        {
            SignalRelay generator = Make<SignalRelay>("gen2");
            var gated = new List<Switch>();
            for (int i = 0; i < 4; i++)
            {
                Switch sw = Make<Switch>("device" + i);
                sw.gameObject.AddComponent<BlockedBy>().Configure(generator, sw, "Needs power", false);
                gated.Add(sw);
            }

            bool allBlocked = true;
            for (int i = 0; i < gated.Count; i++)
                allBlocked &= gated[i].Blocked;

            generator.SetState(true);

            bool allClear = true;
            for (int i = 0; i < gated.Count; i++)
                allClear &= !gated[i].Blocked;

            Check("8. One source gates many devices, holding no list", allBlocked && allClear);
        }

        private void Recipe9RelayEscapeHatch()
        {
            SignalRelay relay = Make<SignalRelay>("relay");
            bool fired = false;
            relay.StateChanged += _ => fired = true;

            relay.SetState(true);
            Check("9. Relay hands state to arbitrary listeners", fired);
        }

        private void Recipe10ChainedNodes()
        {
            Switch sw = Make<Switch>("chainSwitch");
            SignalRelay middle = Make<SignalRelay>("chainRelay");
            Door door = Make<Door>("chainDoor");

            sw.AddOutput(middle, OutputMode.Follow);
            middle.AddOutput(door, OutputMode.Follow);

            sw.Interact(this);
            Check("10. State propagates down a chain", door.State);
        }

        private void Recipe11SignalVisual()
        {
            GameObject cube = GameObject.CreatePrimitive(PrimitiveType.Cube);
            cube.name = "visual";
            _spawned.Add(cube);

            // SignalVisual observes a node rather than being one, so a switch on the same object
            // drives it with nothing wired. That is the behaviour being checked here.
            Switch sw = cube.AddComponent<Switch>();
            sw.Configure("Light", SwitchMode.Toggle);
            SignalVisual visual = cube.AddComponent<SignalVisual>();
            visual.Configure(null, cube.GetComponent<Renderer>(), Color.gray, Color.green);

            sw.Interact(this);

            // The tint goes through a MaterialPropertyBlock, so reading it back confirms the write
            // landed without instantiating (and leaking) a per-object material.
            var block = new MaterialPropertyBlock();
            cube.GetComponent<Renderer>().GetPropertyBlock(block);
            Color tint = block.GetColor(Shader.PropertyToID("_BaseColor"));
            Check("11. SignalVisual follows its node with no wiring", visual.Source == sw && tint.g > tint.r);
        }

        private IEnumerator Recipe12SocketSeatsAProp()
        {
            GameObject socketGo = GameObject.CreatePrimitive(PrimitiveType.Cube);
            socketGo.name = "socket";
            _spawned.Add(socketGo);
            socketGo.transform.position = new Vector3(0f, -50f, 0f);
            socketGo.GetComponent<Collider>().isTrigger = true;

            Socket socket = socketGo.AddComponent<Socket>();
            socket.Configure("battery", null, true);

            Door door = Make<Door>("socketDoor");
            door.transform.position = new Vector3(0f, -50f, 5f);
            socket.AddOutput(door, OutputMode.Follow);

            GameObject propGo = GameObject.CreatePrimitive(PrimitiveType.Cube);
            propGo.name = "battery";
            _spawned.Add(propGo);
            propGo.transform.localScale = Vector3.one * 0.3f;
            propGo.transform.position = new Vector3(0f, -50f, 0f);
            propGo.AddComponent<Rigidbody>().useGravity = false;
            propGo.AddComponent<Carryable>().Configure("battery");

            // Physics triggers need a fixed step to register; waiting two is enough and keeps the
            // test from depending on the project's timestep.
            yield return new WaitForFixedUpdate();
            yield return new WaitForFixedUpdate();

            Check("12. Socket seats a matching prop and drives its output", socket.State && door.State);
        }

        // --- plumbing -----------------------------------------------------------------------

        private T Make<T>(string name) where T : Component
        {
            var go = new GameObject(name);
            // Parked far below the scene so the self-test's throwaway objects never collide with,
            // or visually clutter, the hand-authored showcase running in the same scene.
            go.transform.position = new Vector3(0f, -50f, 0f);
            _spawned.Add(go);
            return go.AddComponent<T>();
        }

        private void Check(string label, bool condition)
        {
            _results.Add((condition ? "PASS  " : "FAIL  ") + label);
            if (condition)
                _passed++;
            else
                _failed++;
        }

        private void Report()
        {
            var sb = new StringBuilder();
            sb.Append("[InteractionKit SelfTest] ")
              .Append(_failed == 0 ? "PASS" : "FAIL")
              .Append(" - ").Append(_passed).Append(" passed, ").Append(_failed).Append(" failed.");

            for (int i = 0; i < _results.Count; i++)
                sb.Append('\n').Append("  ").Append(_results[i]);

            if (_failed == 0)
                Debug.Log(sb.ToString());
            else
                Debug.LogError(sb.ToString());
        }

        private void Cleanup()
        {
            // Disable before destroying. Destroy is deferred to end of frame, so a node whose
            // outputs are torn down in a different order can still emit into a half-destroyed peer;
            // disabling first runs every OnDisable and unsubscribes the observers cleanly.
            for (int i = 0; i < _spawned.Count; i++)
                if (_spawned[i] != null)
                    _spawned[i].SetActive(false);

            for (int i = 0; i < _spawned.Count; i++)
                if (_spawned[i] != null)
                    Destroy(_spawned[i]);

            _spawned.Clear();
        }
    }
}
