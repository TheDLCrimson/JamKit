using System;
using System.Collections.Generic;
using UnityEngine;

namespace JamKit.InteractionKit
{
    /// <summary>
    /// Base of the whole kit: an object that holds one boolean state and propagates it to the
    /// nodes it is wired to.
    ///
    /// Everything interactive is one of these. A switch is on or off, a door is open or closed, a
    /// socket is filled or empty, a gate is satisfied or not. Rooms are authored by dragging nodes
    /// into each other's Outputs list instead of writing per-room glue code.
    ///
    /// Wiring is push, source-side: a node drives its targets. <see cref="Gate"/> is the one
    /// deliberate exception and pulls from an authored input list instead, because a scene-level
    /// Gate has to read nodes that live inside room prefabs, and a reference pointing out of a
    /// prefab does not survive being applied. See the note on that class.
    /// </summary>
    /// <remarks>
    /// v2 note: <c>Id</c> and <c>Blocked</c> used to live on a separate <c>Interactable</c> base in
    /// the Interactables module, so almost every real object stacked one of each. They are merged in
    /// here because the mechanic that justified keeping them apart (tape replay and remote hacking,
    /// which needed to address an object by id and re-fire it idempotently) was cut. There is no
    /// registry: <c>Id</c> is for objectives and save/load, both of which hold direct references.
    /// </remarks>
    public abstract class SignalNode : MonoBehaviour
    {
        [Tooltip("Optional stable identifier, for objectives and save/load. Leave empty unless something addresses this node by name. Not required to be unique and not indexed anywhere.")]
        [SerializeField] private string _id = "";

        [Tooltip("Current state. Serialized so it doubles as the authored starting state and stays visible live during play.")]
        [SerializeField] protected bool _state;

        [Tooltip("Nodes this one drives when its state changes.")]
        [SerializeField] private List<Output> _outputs = new List<Output>();

        /// <summary>Optional stable identifier. Empty on most nodes.</summary>
        public string Id => _id;

        /// <summary>Current state of this node.</summary>
        public bool State => _state;

        /// <summary>
        /// True while this node refuses interaction, raised by <see cref="BlockedBy"/>.
        /// Advisory only: the flag is raised here, but <see cref="Interactor"/> is what actually
        /// refuses the hold.
        /// </summary>
        public bool Blocked { get; private set; }

        /// <summary>Fired after the state actually changes. Not fired when set to the value it already holds.</summary>
        public event Action<bool> StateChanged;

        // Tracks the state OnValidate last pushed, so it can tell "the Inspector really edited _state"
        // apart from "Unity just (re)deserialized this object" - the latter happens on every Play
        // entry, not just a hand-edit, and would otherwise re-fire OnStateChanged for every node in
        // the scene the instant Play starts, often before other systems (e.g. AudioService) have
        // finished initializing. Kept in sync by SetState too, so a later Inspector edit is compared
        // against the true last-applied value rather than a stale one from Awake.
        [NonSerialized] private bool _lastValidatedState;
        [NonSerialized] private bool _hasValidatedState;

        protected virtual void Awake()
        {
            _lastValidatedState = _state;
            _hasValidatedState = true;
        }

        /// <summary>
        /// Sets the state and propagates to every wired output. No-op when the value is unchanged,
        /// which is also what stops a wiring cycle from recursing forever.
        /// </summary>
        public void SetState(bool value)
        {
            if (_state == value)
                return;

            _state = value;
            _lastValidatedState = value;
            OnStateChanged();
            StateChanged?.Invoke(_state);
            Emit();
        }

        /// <summary>Toggles the state.</summary>
        public void Toggle() => SetState(!_state);

        /// <summary>Sets whether interaction is refused. Called by <see cref="BlockedBy"/>.</summary>
        public void SetBlocked(bool blocked) => Blocked = blocked;

        /// <summary>Assigns the id from code (demos and tests); in the editor set the serialized field.</summary>
        public void SetId(string id) => _id = id;

        /// <summary>Wires a connection from code (demos and runtime-authored rooms); in the editor use the Outputs list.</summary>
        public void AddOutput(SignalNode target, OutputMode mode)
        {
            if (target == null || target == this)
                return;
            _outputs.Add(new Output { Target = target, Mode = mode });
        }

        /// <summary>Removes every wired connection.</summary>
        public void ClearOutputs() => _outputs.Clear();

        /// <summary>How many outputs are wired, including any empty rows. Used by the validator.</summary>
        public int OutputCount => _outputs.Count;

        /// <summary>The target at <paramref name="index"/>, or null for an empty row. Used by the validator and the editor.</summary>
        public SignalNode OutputTargetAt(int index) =>
            index >= 0 && index < _outputs.Count ? _outputs[index].Target : null;

        /// <summary>
        /// Called after the state changed and before outputs fire, so a node can react to its own
        /// state (<see cref="Door"/> starts moving, <see cref="SignalRelay"/> invokes its UnityEvent).
        /// </summary>
        protected virtual void OnStateChanged() { }

        /// <summary>
        /// Pushes the current state along every output row. Called automatically on change; call it
        /// directly only when a node needs to re-assert its state without changing it.
        /// </summary>
        protected void Emit()
        {
            for (int i = 0; i < _outputs.Count; i++)
            {
                SignalNode target = _outputs[i].Target;
                if (target == null || target == this)
                    continue;

                switch (_outputs[i].Mode)
                {
                    case OutputMode.Follow:
                        target.SetState(_state);
                        break;

                    case OutputMode.Pulse:
                        if (_state)
                            target.SetState(true);
                        break;
                }
            }
        }

        // Wiring is invisible in the scene view without this, and a mis-wired link is the most
        // likely authoring mistake. Cheapest debugging tool in the kit.
        //
        // Selecting a node always shows its wiring. The Show Wiring toggle additionally draws every
        // node's wiring without selecting anything, which is how you audit a whole room at a glance.
        //
        // These are deliberately NOT declared in the leaves. Unity dispatches gizmo messages to the
        // most-derived declaration only, so a leaf that declared its own OnDrawGizmosSelected would
        // silently hide this one and stop drawing wiring entirely. Leaves override DrawNodeGizmos.
        private void OnDrawGizmosSelected()
        {
            DrawWiringGizmos();
            DrawNodeGizmos();
        }

        private void OnDrawGizmos()
        {
            if (!InteractionGizmos.ShowWiring)
                return;

            DrawWiringGizmos();
            DrawNodeGizmos();
        }

        /// <summary>Leaf-specific scene-view gizmos. Override instead of declaring OnDrawGizmos*.</summary>
        protected virtual void DrawNodeGizmos() { }

        private void DrawWiringGizmos()
        {
            Vector3 from = transform.position;
            Gizmos.color = _state ? new Color(0.2f, 0.95f, 0.35f) : new Color(0.55f, 0.55f, 0.6f);

            for (int i = 0; i < _outputs.Count; i++)
            {
                SignalNode target = _outputs[i].Target;
                if (target == null || target == this)
                    continue;

                Vector3 to = target.transform.position;
                Gizmos.DrawLine(from, to);
                Gizmos.DrawSphere(Vector3.Lerp(from, to, 0.92f), 0.06f);
            }
        }

#if UNITY_EDITOR
        // Ticking the serialized _state field in the inspector assigns the field directly and so
        // bypasses SetState. During play that would look like a broken link, so re-push here - but
        // only once Awake has run and only if _state actually differs from the last value this node
        // applied, since OnValidate also fires (a) for an edit to ANY field on this component, not
        // just _state, and (b) once per node right as Play begins, before Awake.
        // Editor convenience only; has no effect in a build.
        protected virtual void OnValidate()
        {
            if (!Application.isPlaying || !_hasValidatedState || _state == _lastValidatedState)
                return;

            _lastValidatedState = _state;
            OnStateChanged();
            Emit();
        }

        [ContextMenu("Toggle State")]
        private void ContextToggle() => Toggle();
#endif
    }
}
