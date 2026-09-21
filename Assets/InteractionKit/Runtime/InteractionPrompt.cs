using TMPro;
using UnityEngine;

namespace JamKit.InteractionKit
{
    /// <summary>
    /// The one world-space label presenter on the player rig. Shows "[hold E] Open Door" over the
    /// offered interactable, or "[F] PICK UP" over a grabbable prop, and nothing when neither is live.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Player-owned, not per-object. In v1 there were two presenters: <c>HoldToInteract</c> built a
    /// label per interactable (so prompt styling was authored once per object in the project), and
    /// <c>CarryPrompt</c> already sat on the rig for carryables, because a prop needs no component
    /// to be grabbable and so has nothing to hang a per-object prompt off. CarryPrompt's header
    /// called itself the shape HoldToInteract should adopt. This is that merge.
    /// </para>
    /// <para>
    /// It renders state the Interactor and the carrier have already published, and never probes for
    /// itself. That is what makes it impossible for the label and the hover outline to disagree
    /// about which object is live - both read the same two sources, in the same precedence.
    /// </para>
    /// <para>
    /// Both key names come from the components actually polling them, so a label can never name a
    /// binding that has been rebound somewhere else.
    /// </para>
    /// </remarks>
    [AddComponentMenu("JamKit/Interaction/Interaction Prompt")]
    [DisallowMultipleComponent]
    public sealed class InteractionPrompt : MonoBehaviour
    {
        [Tooltip("The interactor whose offered target is labelled. Empty = the one on this GameObject.")]
        [SerializeField] private Interactor _interactor;

        [Tooltip("Optional. The carry component whose hover/held prop is labelled. Empty = an ICarrier on this GameObject, if any. Carry prompts are skipped entirely when there is none.")]
        [SerializeField] private MonoBehaviour _carrier;

        [Header("Carry labels")]
        [Tooltip("Shown over a prop the player is aiming at while empty-handed.")]
        [SerializeField] private string _pickUpLabel = "PICK UP";

        [Tooltip("Shown over the prop currently being carried.")]
        [SerializeField] private string _dropLabel = "DROP";

        [Header("Placement")]
        [Tooltip("Clearance between the top of the target's bounds and the prompt, in world metres.")]
        [SerializeField] private float _verticalMargin = 0.18f;

        [Tooltip("How far the prompt is pulled toward the camera off its anchor. Sitting the label in " +
                 "the object's own plane makes it read as painted onto the surface whenever you look " +
                 "down at a low prop, which is the normal way to aim at one.")]
        [SerializeField] private float _cameraPull = 0.3f;

        [Header("Presentation")]
        [Tooltip("World height of one line of prompt text, before the constant-screen-size scaling below.")]
        [SerializeField] private float _promptSize = 0.045f;

        [Tooltip("Font the prompt renders in. Empty = the project's TMP default font asset.")]
        [SerializeField] private TMP_FontAsset _promptFont;

        [SerializeField] private Color _idleColor = new Color(0.2f, 0.9f, 0.3f);

        [Tooltip("While a hold is running, and while carrying. Amber consistently means \"a commitment is in progress\".")]
        [SerializeField] private Color _holdingColor = new Color(1f, 0.85f, 0.25f);

        [Tooltip("While the offered interactable is blocked (see BlockedBy).")]
        [SerializeField] private Color _blockedColor = new Color(0.95f, 0.35f, 0.3f);

        [Tooltip("Keep the prompt the same apparent size on screen regardless of distance.")]
        [SerializeField] private bool _constantScreenSize = true;

        [SerializeField] private float _referenceDistance = 2.5f;

        [Tooltip("Camera the prompt faces. Empty = Camera.main at Start.")]
        [SerializeField] private Camera _camera;

        private TMP_Text _prompt;
        private Transform _anchor;

        // Cached when the anchor changes: the prompt sits above the target's full visual bounds, and
        // props are physics bodies in motion, so the bounds are recomposed every frame from these.
        private Renderer[] _anchorRenderers;
        private ICarrier _carry;

        private void Awake()
        {
            if (_interactor == null)
                _interactor = GetComponent<Interactor>();

            _carry = _carrier as ICarrier;
            if (_carry == null)
            {
                MonoBehaviour[] local = GetComponents<MonoBehaviour>();
                for (int i = 0; i < local.Length; i++)
                {
                    if (local[i] is ICarrier found)
                    {
                        _carry = found;
                        break;
                    }
                }
            }
        }

        private void Start()
        {
            if (_camera == null)
                _camera = Camera.main;
            BuildPrompt();
        }

        private void OnDisable() => Show(false);

        private void OnDestroy()
        {
            // The prompt is parentless, so nothing else would tear it down with the player.
            if (_prompt != null)
                Destroy(_prompt.gameObject);
        }

        // LateUpdate so a carried prop has already been moved by this frame's physics follow, and so
        // this runs alongside HoverHighlight's own LateUpdate rather than a frame behind it.
        private void LateUpdate()
        {
            if (_prompt == null)
                return;

            // Carry wins, exactly as the hover outline does: you can only grab what you are aiming
            // at, and a prop in your hands is the most immediate thing you can act on.
            Rigidbody carryTarget = null;
            if (_carry != null)
                carryTarget = _carry.IsCarrying ? _carry.Held : _carry.HoverTarget;

            if (carryTarget != null)
            {
                Present(carryTarget.transform,
                    "[" + _carry.CarryKeyDisplay + "] " + (_carry.IsCarrying ? _dropLabel : _pickUpLabel),
                    _carry.IsCarrying ? _holdingColor : _idleColor);
                return;
            }

            IInteractable target = _interactor != null ? _interactor.Current : null;
            if (target == null)
            {
                Show(false);
                return;
            }

            string key = _interactor.KeyDisplay;
            string text;
            Color color;

            if (target.Blocked)
            {
                // Silence is a legitimate authored choice, so an empty message shows the label alone.
                string reason = target.BlockedMessage;
                color = _blockedColor;
                text = string.IsNullOrEmpty(reason) ? target.Label : target.Label + "\n" + reason;
            }
            else if (_interactor.IsHolding)
            {
                color = _holdingColor;
                text = "[" + key + "] " + target.Label + "\n" + ProgressBar(_interactor.Progress);
            }
            else
            {
                color = _idleColor;
                text = "[hold " + key + "] " + target.Label;
            }

            Present(target.transform, text, color);
        }

        /// <summary>Sets the camera the prompt faces. Defaults to Camera.main; set when the view camera changes.</summary>
        public void SetBillboardCamera(Camera camera) => _camera = camera;

        private void Present(Transform anchor, string text, Color color)
        {
            if (anchor != _anchor)
            {
                _anchor = anchor;
                _anchorRenderers = anchor != null ? anchor.GetComponentsInChildren<Renderer>() : null;
            }

            Vector3 position;
            if (_anchor == null || !TryAnchorPoint(out position))
            {
                Show(false);
                return;
            }

            Show(true);
            _prompt.text = text;
            _prompt.color = color;

            Camera cam = _camera != null ? _camera : Camera.main;
            if (cam == null)
            {
                _prompt.transform.position = position;
                return;
            }

            // Never above eye level. Sitting the label on top of the object reads well for a stool,
            // but a 2 m tall cabinet's top is above the player's eye - and you look DOWN at a low
            // prop to aim at it, which put the label off the top of the screen entirely. Clamping to
            // eye height is the ergonomic rule stated directly, and it costs nothing on an object
            // short enough for the top-of-bounds anchor to already be below the eye.
            if (position.y > cam.transform.position.y)
                position.y = cam.transform.position.y;

            // Then lift it off the object toward the viewer, so it floats in front rather than
            // intersecting the silhouette. After the clamp, so the pull is measured from where the
            // label actually ends up.
            Vector3 toCamera = cam.transform.position - position;
            if (toCamera.sqrMagnitude > 0.0001f)
                position += toCamera.normalized * _cameraPull;

            _prompt.transform.position = position;
            _prompt.transform.rotation = cam.transform.rotation;

            // Apparent size on screen is worldSize/distance, so scaling with distance holds it constant.
            if (_constantScreenSize)
            {
                float distance = Vector3.Distance(cam.transform.position, position);
                float scale = Mathf.Clamp(distance / Mathf.Max(0.01f, _referenceDistance), 0.25f, 4f);
                _prompt.transform.localScale = Vector3.one * scale;
            }
            else
            {
                _prompt.transform.localScale = Vector3.one;
            }
        }

        // Just above the target's combined visual bounds, so one offset reads correctly on a low
        // stool and a tall cabinet alike instead of being authored per object.
        private bool TryAnchorPoint(out Vector3 point)
        {
            point = default;
            if (_anchor == null)
                return false;

            bool any = false;
            Bounds bounds = default;
            if (_anchorRenderers != null)
            {
                for (int i = 0; i < _anchorRenderers.Length; i++)
                {
                    Renderer r = _anchorRenderers[i];
                    if (r == null || !r.enabled)
                        continue;

                    if (!any)
                    {
                        bounds = r.bounds;
                        any = true;
                    }
                    else
                    {
                        bounds.Encapsulate(r.bounds);
                    }
                }
            }

            // An object whose mesh lives somewhere unusual still gets a prompt, just at its pivot.
            point = any
                ? new Vector3(bounds.center.x, bounds.max.y + _verticalMargin, bounds.center.z)
                : _anchor.position + Vector3.up * _verticalMargin;
            return true;
        }

        private static string ProgressBar(float progress)
        {
            const int Slots = 12;
            int filled = Mathf.RoundToInt(Mathf.Clamp01(progress) * Slots);
            return new string('|', filled).PadRight(Slots, '.');
        }

        private void Show(bool visible)
        {
            if (_prompt != null && _prompt.gameObject.activeSelf != visible)
                _prompt.gameObject.SetActive(visible);
        }

        private void BuildPrompt()
        {
            var go = new GameObject(name + "_InteractionPrompt");

            var tmp = go.AddComponent<TextMeshPro>();
            tmp.alignment = TextAlignmentOptions.Center;
            tmp.textWrappingMode = TextWrappingModes.NoWrap;
            // TMP world text sizes an em at fontSize/10 metres; the legacy TextMesh this replaced
            // sized one at characterSize * 48 * 0.1. Carrying that 48 over keeps every _promptSize
            // already authored across existing scenes rendering at the size it was tuned to.
            tmp.fontSize = _promptSize * 48f;
            tmp.color = _idleColor;
            if (_promptFont != null)
                tmp.font = _promptFont;
            // Wide enough that a two-line prompt is never clipped; NoWrap keeps the rect off the layout.
            tmp.rectTransform.sizeDelta = new Vector2(8f, 2f);

            _prompt = tmp;
            go.SetActive(false);
        }
    }
}
