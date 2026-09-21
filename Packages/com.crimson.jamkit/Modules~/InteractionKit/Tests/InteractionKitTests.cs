using System.Collections.Generic;
using NUnit.Framework;
using UnityEngine;

namespace JamKit.InteractionKit.Tests
{
    /// <summary>
    /// EditMode coverage for the v2 merge. Ports the Interactables module cases that still apply
    /// (switch verbs collapse to one, doors, gating) and adds the cases the merge itself introduces.
    /// </summary>
    /// <remarks>
    /// EditMode means Awake does not run, so anything depending on it is either exercised through
    /// the public API or covered by the play-mode suite instead. Each test builds its own objects
    /// and tears them down, so no ordering dependency exists between them.
    /// </remarks>
    public sealed class InteractionKitTests
    {
        private readonly List<GameObject> _spawned = new List<GameObject>();

        [TearDown]
        public void TearDown()
        {
            for (int i = 0; i < _spawned.Count; i++)
                if (_spawned[i] != null)
                    Object.DestroyImmediate(_spawned[i]);
            _spawned.Clear();
        }

        private T New<T>(string name) where T : Component
        {
            var go = new GameObject(name);
            _spawned.Add(go);
            return go.AddComponent<T>();
        }

        // --- SignalNode core ---------------------------------------------------------------

        [Test]
        public void SetState_ChangesStateAndFiresEventOnce()
        {
            SignalRelay node = New<SignalRelay>("node");
            int fired = 0;
            node.StateChanged += _ => fired++;

            node.SetState(true);
            Assert.IsTrue(node.State);
            Assert.AreEqual(1, fired);
        }

        [Test]
        public void SetState_ToSameValue_IsANoOp()
        {
            SignalRelay node = New<SignalRelay>("node");
            node.SetState(true);

            int fired = 0;
            node.StateChanged += _ => fired++;
            node.SetState(true);

            Assert.AreEqual(0, fired, "Re-setting the same value must not re-fire; this is what stops a wiring cycle recursing.");
        }

        [Test]
        public void WiringCycle_DoesNotRecurseForever()
        {
            SignalRelay a = New<SignalRelay>("a");
            SignalRelay b = New<SignalRelay>("b");
            a.AddOutput(b, OutputMode.Follow);
            b.AddOutput(a, OutputMode.Follow);

            a.SetState(true);

            Assert.IsTrue(a.State);
            Assert.IsTrue(b.State);
        }

        [Test]
        public void Follow_MirrorsBothDirections()
        {
            SignalRelay source = New<SignalRelay>("source");
            SignalRelay target = New<SignalRelay>("target");
            source.AddOutput(target, OutputMode.Follow);

            source.SetState(true);
            Assert.IsTrue(target.State);

            source.SetState(false);
            Assert.IsFalse(target.State, "Follow mirrors the source, so turning the source off turns the target off.");
        }

        [Test]
        public void Pulse_SetsOnRisingEdgeOnly()
        {
            SignalRelay source = New<SignalRelay>("source");
            SignalRelay target = New<SignalRelay>("target");
            source.AddOutput(target, OutputMode.Pulse);

            source.SetState(true);
            Assert.IsTrue(target.State);

            source.SetState(false);
            Assert.IsTrue(target.State, "Pulse leaves the target alone on the falling edge.");
        }

        [Test]
        public void AddOutput_RefusesSelfAndNull()
        {
            SignalRelay node = New<SignalRelay>("node");
            node.AddOutput(node, OutputMode.Follow);
            node.AddOutput(null, OutputMode.Follow);

            Assert.AreEqual(0, node.OutputCount);
        }

        // --- Switch (the SwitchInteractable + Switchable merge) ----------------------------

        [Test]
        public void Switch_Toggle_FlipsEachInteraction()
        {
            Switch sw = New<Switch>("switch");
            sw.Configure("Use", SwitchMode.Toggle);

            sw.Interact(null);
            Assert.IsTrue(sw.State);

            sw.Interact(null);
            Assert.IsFalse(sw.State);
        }

        [Test]
        public void Switch_Once_StaysOnAfterRepeatedInteraction()
        {
            Switch sw = New<Switch>("switch");
            sw.Configure("Use", SwitchMode.Once);

            sw.Interact(null);
            sw.Interact(null);

            Assert.IsTrue(sw.State, "Once latches; later interactions do nothing.");
        }

        [Test]
        public void Switch_DrivesItsOutput_WithNoSeparateTranslatorComponent()
        {
            // The whole point of the merge: in v1 this needed SwitchInteractable + Switchable and a
            // same-GameObject reference between them.
            Switch sw = New<Switch>("switch");
            Door door = New<Door>("door");
            sw.Configure("Open Door", SwitchMode.Toggle);
            sw.AddOutput(door, OutputMode.Follow);

            sw.Interact(null);

            Assert.IsTrue(door.State);
        }

        [Test]
        public void Switch_ExposesLabelAndHoldOverrideThroughInterface()
        {
            Switch sw = New<Switch>("switch");
            sw.Configure("Open Door", SwitchMode.Toggle, 0.35f, 2.5f);

            IInteractable asInterface = sw;
            Assert.AreEqual("Open Door", asInterface.Label);
            Assert.AreEqual(2.5f, asInterface.HoldSecondsOverride);
        }

        // --- BlockedBy (replaces PoweredInteractable) --------------------------------------

        [Test]
        public void BlockedBy_BlocksWhileSourceIsOff_AndClearsWhenItTurnsOn()
        {
            SignalRelay power = New<SignalRelay>("power");
            Switch gated = New<Switch>("gated");

            BlockedBy block = gated.gameObject.AddComponent<BlockedBy>();
            block.Configure(power, gated, "Needs power", false);

            Assert.IsTrue(gated.Blocked, "Blocked while the source is off.");

            power.SetState(true);
            Assert.IsFalse(gated.Blocked);
        }

        [Test]
        public void BlockedBy_RefusesInteraction()
        {
            SignalRelay power = New<SignalRelay>("power");
            Switch gated = New<Switch>("gated");
            BlockedBy block = gated.gameObject.AddComponent<BlockedBy>();
            block.Configure(power, gated, "Needs power", false);

            gated.Interact(null);

            Assert.IsFalse(gated.State, "A blocked switch must not change state.");
        }

        [Test]
        public void BlockedBy_OneSourceGatesManyTargets()
        {
            // This is what removed PoweredInteractable's single-target limit: the reference points
            // from the gated object to its source, so the source holds no list.
            SignalRelay power = New<SignalRelay>("power");
            var gated = new List<Switch>();

            for (int i = 0; i < 5; i++)
            {
                Switch sw = New<Switch>("gated" + i);
                sw.gameObject.AddComponent<BlockedBy>().Configure(power, sw, "Needs power", false);
                gated.Add(sw);
            }

            for (int i = 0; i < gated.Count; i++)
                Assert.IsTrue(gated[i].Blocked);

            power.SetState(true);

            for (int i = 0; i < gated.Count; i++)
                Assert.IsFalse(gated[i].Blocked);
        }

        // --- Door --------------------------------------------------------------------------

        [Test]
        public void Door_AuthoredTransformIsTheClosedPose()
        {
            Door door = New<Door>("door");
            door.transform.localPosition = new Vector3(5f, 0f, 0f);
            door.Configure(new Vector3(0f, 3f, 0f), 0.5f);

            door.SnapTo(false);

            Assert.AreEqual(new Vector3(5f, 0f, 0f), door.transform.localPosition,
                "Closed must be exactly where the door was authored.");
        }

        [Test]
        public void Door_SnapToOpen_AppliesTheLocalOffset()
        {
            Door door = New<Door>("door");
            door.PreviewAt(0f, Vector3.zero, Quaternion.identity);
            door.Configure(new Vector3(0f, 3f, 0f), 0.5f);

            door.PreviewAt(1f, Vector3.zero, Quaternion.identity);

            Assert.AreEqual(3f, door.transform.localPosition.y, 0.001f);
        }

        [Test]
        public void Door_OffsetIsLocalSpace_SoARotatedParentStillOpensCorrectly()
        {
            var parent = new GameObject("room");
            _spawned.Add(parent);
            parent.transform.rotation = Quaternion.Euler(0f, 90f, 0f);

            Door door = New<Door>("door");
            door.transform.SetParent(parent.transform, false);
            door.Configure(new Vector3(0f, 3f, 0f), 0.5f);
            door.PreviewAt(1f, Vector3.zero, Quaternion.identity);

            Assert.AreEqual(3f, door.transform.localPosition.y, 0.001f,
                "The offset is applied in local space, so a rotated room prefab still opens along its own axis.");
        }

        // --- Gate --------------------------------------------------------------------------

        [Test]
        public void Gate_ReportsThresholdAndInputCount()
        {
            SignalRelay a = New<SignalRelay>("a");
            SignalRelay b = New<SignalRelay>("b");
            SignalRelay c = New<SignalRelay>("c");
            Gate gate = New<Gate>("gate");
            gate.Configure(3, false, a, b, c);

            Assert.AreEqual(3, gate.InputCount);
            Assert.AreEqual(3, gate.Threshold);
            Assert.IsFalse(gate.Invert);
        }

        [Test]
        public void Gate_InputAt_ReturnsNullForAnEmptyRow()
        {
            Gate gate = New<Gate>("gate");
            gate.Configure(1, false, null, null);

            Assert.AreEqual(2, gate.InputCount);
            Assert.IsNull(gate.InputAt(0), "An empty row reads as null, which is what the validator counts.");
            Assert.IsNull(gate.InputAt(99), "Out of range reads as null rather than throwing.");
        }

        // --- Carryable ---------------------------------------------------------------------

        [Test]
        public void Carryable_DefaultsToCarryable()
        {
            Carryable prop = New<Carryable>("prop");
            Assert.IsTrue(prop.CanBeCarried,
                "Adding the component must make a prop work immediately; the box is there to disable one.");
        }

        [Test]
        public void Carryable_ConfigureSetsIdAndTickbox()
        {
            Carryable prop = New<Carryable>("prop");
            prop.Configure("battery", false);

            Assert.AreEqual("battery", prop.Id);
            Assert.IsFalse(prop.CanBeCarried);
        }

        // --- Interactor arbitration --------------------------------------------------------

        [Test]
        public void Interactor_RegistryIgnoresDuplicates()
        {
            Switch sw = New<Switch>("switch");

            Interactor.Register(sw);
            Interactor.Register(sw);
            Interactor.Unregister(sw);

            // A single Unregister must fully remove it, or a stale entry survives and the prompt
            // offers a destroyed object.
            Interactor interactor = New<Interactor>("interactor");
            var origin = new GameObject("origin");
            _spawned.Add(origin);
            interactor.Configure(origin.transform, 100f, 0f);

            Assert.DoesNotThrow(() => Interactor.Unregister(sw));
        }

        [Test]
        public void Interactor_UnregisterOfUnknownIsSafe()
        {
            Switch sw = New<Switch>("switch");
            Assert.DoesNotThrow(() => Interactor.Unregister(sw));
        }
    }
}
