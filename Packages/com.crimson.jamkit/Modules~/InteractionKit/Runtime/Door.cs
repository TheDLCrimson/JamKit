using UnityEngine;

namespace JamKit.InteractionKit
{
    /// <summary>
    /// A node that moves itself when it turns on. Doors, drawers, hatches, lids, blast shutters.
    /// Slides, swings, or both at once.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The offset is deliberately LOCAL space. A world-space offset silently breaks the moment a
    /// room prefab is rotated, and rotated room prefabs are the whole point of authoring rooms as
    /// prefabs, so this is the one detail here worth being fussy about.
    /// </para>
    /// <para>
    /// The authored transform position is always the CLOSED pose. Starting with State ticked on
    /// snaps to the open pose on Awake rather than treating the authored position as open, so a
    /// designer who leaves a door open in the scene view does not bake a wrong closed position.
    /// </para>
    /// <para>
    /// v2 note: replaces both <c>Openable</c> and the Interactables module's <c>DoorInteractable</c>,
    /// which were two separate doors with different motion models. This keeps Openable's local-space
    /// offset and pre-multiplied swing, and DoorInteractable's guarantee of exact arrival (progress
    /// is driven by MoveTowards, so it reaches its target rather than approaching it forever).
    /// </para>
    /// </remarks>
    [AddComponentMenu("JamKit/Interaction/Door")]
    public sealed class Door : SignalNode
    {
        [Tooltip("Local-space offset from the closed pose to the open pose. Leave zero for a pure swing.")]
        [SerializeField] private Vector3 _openLocalOffset = new Vector3(0f, 3f, 0f);

        [Tooltip("Euler rotation from the closed pose to the open pose, in degrees. Applied about the " +
                 "PARENT's axes and around this transform's own origin, so a hinged leaf whose origin " +
                 "sits on its hinge swings correctly regardless of how the leaf itself is rotated.")]
        [SerializeField] private Vector3 _openLocalEuler;

        [Tooltip("Seconds to travel between closed and open.")]
        [SerializeField] private float _moveDuration = 0.6f;

        [Tooltip("Transform to move. Empty = this object's transform.")]
        [SerializeField] private Transform _moved;

        /// <summary>Travel progress: 0 fully closed, 1 fully open.</summary>
        public float Progress => _progress;

        /// <summary>True once the door has finished travelling to its current state.</summary>
        public bool IsSettled => Mathf.Approximately(_progress, _state ? 1f : 0f);

        /// <summary>Local-space offset from closed to open. Read by the editor's Preview Open.</summary>
        public Vector3 OpenLocalOffset => _openLocalOffset;

        /// <summary>Euler swing from closed to open. Read by the editor's Preview Open.</summary>
        public Vector3 OpenLocalEuler => _openLocalEuler;

        /// <summary>The transform actually moved. Read by the editor's Preview Open.</summary>
        public Transform Moved => _moved != null ? _moved : transform;

        private Vector3 _closedLocalPosition;
        private Quaternion _closedLocalRotation;
        private float _progress;

        // Whether the closed pose has been read off the transform yet. Without this, _closedLocal*
        // stay at their zero defaults until Awake runs, so any SnapTo or pose call before that
        // teleports the door to the origin instead of leaving it where it was authored.
        [System.NonSerialized] private bool _poseCaptured;

        protected override void Awake()
        {
            base.Awake();

            if (_moved == null)
                _moved = transform;

            CaptureClosedPose();
            _progress = _state ? 1f : 0f;
            ApplyPose();
        }

        // The authored transform is the closed pose by contract, so capturing it lazily is always
        // correct: in play mode Awake gets there first, and everywhere else (editor preview, tests,
        // a SnapTo from a script's own Awake) the transform still holds the authored values.
        private void CaptureClosedPose()
        {
            Transform moved = Moved;
            if (moved == null)
                return;

            _closedLocalPosition = moved.localPosition;
            _closedLocalRotation = moved.localRotation;
            _poseCaptured = true;
        }

        private void Update()
        {
            float target = _state ? 1f : 0f;
            if (Mathf.Approximately(_progress, target))
                return;

            // Duration-based rather than MathUtil.Decay: a door needs to actually arrive, and an
            // exponential approach never quite does.
            float step = _moveDuration <= 0f ? 1f : Time.deltaTime / _moveDuration;
            _progress = Mathf.MoveTowards(_progress, target, step);
            ApplyPose();
        }

        /// <summary>Code-built setup (demos, tests and runtime-authored rooms); in the editor set the serialized fields.</summary>
        public void Configure(Vector3 openLocalOffset, float moveDuration)
        {
            _openLocalOffset = openLocalOffset;
            _moveDuration = moveDuration;
        }

        /// <summary>As <see cref="Configure(Vector3,float)"/>, plus a swing. Use for hinged leaves.</summary>
        public void Configure(Vector3 openLocalOffset, Vector3 openLocalEuler, float moveDuration)
        {
            _openLocalOffset = openLocalOffset;
            _openLocalEuler = openLocalEuler;
            _moveDuration = moveDuration;
        }

        /// <summary>Opens or closes without animating. Used on Awake and by the editor preview.</summary>
        public void SnapTo(bool open)
        {
            if (!_poseCaptured)
                CaptureClosedPose();

            SetState(open);
            _progress = open ? 1f : 0f;
            ApplyPose();
        }

        /// <summary>
        /// Poses the door at an arbitrary progress without touching state, for the editor's
        /// Preview Open toggle. Captures the closed pose first when called outside play mode,
        /// where Awake has not run.
        /// </summary>
        public void PreviewAt(float progress, Vector3 closedLocalPosition, Quaternion closedLocalRotation)
        {
            _closedLocalPosition = closedLocalPosition;
            _closedLocalRotation = closedLocalRotation;
            _poseCaptured = true;
            _progress = Mathf.Clamp01(progress);
            ApplyPose();
        }

        private void ApplyPose()
        {
            // Resolved through the property, not the raw field. Awake is what normally fills _moved,
            // and Awake has not run in the two cases that matter most: the editor's Preview Open,
            // and EditMode tests. Falling back to this transform makes both behave like play mode
            // instead of silently doing nothing.
            Transform moved = Moved;
            if (moved == null)
                return;

            if (!_poseCaptured)
                CaptureClosedPose();

            float t = Mathf.SmoothStep(0f, 1f, _progress);
            moved.localPosition = _closedLocalPosition + _openLocalOffset * t;

            // Pre-multiplied, so the swing is about the parent's axes rather than the leaf's own.
            // A wardrobe leaf imported with a baked -90 X rotation would otherwise "swing" about an
            // axis pointing along Z. localPosition is untouched, so it pivots about its own origin.
            if (_openLocalEuler != Vector3.zero)
                moved.localRotation = Quaternion.Euler(_openLocalEuler * t) * _closedLocalRotation;
        }

        protected override void DrawNodeGizmos()
        {
            Transform moved = Moved;
            Transform parent = moved.parent;

            Vector3 closedLocal = Application.isPlaying ? _closedLocalPosition : moved.localPosition;
            Vector3 closedWorld = parent != null ? parent.TransformPoint(closedLocal) : closedLocal;
            Vector3 openWorld = parent != null
                ? parent.TransformPoint(closedLocal + _openLocalOffset)
                : closedLocal + _openLocalOffset;

            Gizmos.color = new Color(0.95f, 0.75f, 0.2f);
            Gizmos.DrawLine(closedWorld, openWorld);
            Gizmos.DrawWireSphere(openWorld, 0.12f);
        }
    }
}
