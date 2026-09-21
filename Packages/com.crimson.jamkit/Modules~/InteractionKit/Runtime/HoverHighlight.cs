using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

namespace JamKit.InteractionKit
{
    /// <summary>
    /// Draws a white edge outline around whatever the player is currently hovering, so what a
    /// keypress would act on reads at a glance.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Two sources feed it: the carrier's hover target (the prop a grab would pick up) and the
    /// Interactor's current offer (the interactable whose prompt is showing). Carry wins when both
    /// are live, because you can only grab what you are aiming at.
    /// </para>
    /// <para>
    /// Reading published state rather than probing again is deliberate: the outline and the floating
    /// prompt then physically cannot disagree about which object is live. v2 changed only where that
    /// state comes from - <c>HoldToInteract.Active</c> became <see cref="Interactor.Current"/>, so
    /// there is one arbitration authority instead of a static list on the object side.
    /// </para>
    /// <para>
    /// Presentation only. It drives a pool of reusable "inverted hull" shells: copies of the target's
    /// meshes, scaled up a hair and rendered with front faces culled (so only the silhouette rim
    /// shows through) in an unlit white material. One shell per mesh, so a composite interactable
    /// outlines whole instead of only its first part. Self-contained per object - no URP
    /// renderer-feature or pipeline changes - and non-destructive, since the target's own renderers
    /// are never touched.
    /// </para>
    /// <para>
    /// The shells live at scene root and are driven to match their source transforms each frame,
    /// rather than being parented to the target. That keeps them from being torn down or blocked
    /// from re-parenting when a target is destroyed or the player is disabled.
    /// </para>
    /// </remarks>
    [AddComponentMenu("JamKit/Interaction/Hover Highlight")]
    [DisallowMultipleComponent]
    public sealed class HoverHighlight : MonoBehaviour
    {
        [Tooltip("The interactor whose current offer is outlined. Empty = the one on this GameObject.")]
        [SerializeField] private Interactor _interactor;

        [Tooltip("Optional. The carry component whose hover target is outlined. Empty = an ICarrier on this GameObject, if any. Interactables are outlined either way.")]
        [SerializeField] private MonoBehaviour _carrier;

        [Tooltip("The inverted-hull outline material (URP/Unlit, white, Render Face = Back, i.e. front faces are culled so only the silhouette rim shows). With none assigned the outline is simply disabled.")]
        [SerializeField] private Material _outlineMaterial;

        [Tooltip("Edge thickness in world metres. Each shell is scaled per mesh so the border is this wide on a tiny key and a big crate alike, instead of scaling with the object's size.")]
        [SerializeField] private float _outlineWidth = 0.03f;

        // One inverted hull, matched to a single source mesh.
        private sealed class Shell
        {
            public MeshFilter Filter;
            public MeshRenderer Renderer;
            public Transform Transform;
            public Transform Source;
            public float ScaleFactor;
        }

        private readonly List<Shell> _shells = new List<Shell>();
        private Transform _current;
        private int _activeShells;
        private ICarrier _carry;

        private void Awake()
        {
#if UNITY_EDITOR
            if (_outlineMaterial == null)
                Debug.LogWarning("[HoverHighlight] " + name + " has no outline material, so hover outlines are disabled. " +
                                 "Assign a URP/Unlit material with Render Face = Back.", this);
#endif

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

        private void LateUpdate()
        {
            // Without a material every shell would draw with Unity's magenta error shader, which
            // looks like a broken build rather than an unset optional reference. The outline is
            // presentation only, so the honest behaviour is to draw nothing.
            if (_outlineMaterial == null)
            {
                if (_activeShells > 0)
                    Retarget(null);
                return;
            }

            Transform target = ResolveTarget();

            if (target != _current)
                Retarget(target);

            // Ride along with the sources each frame (they may be physics bodies in motion).
            for (int i = 0; i < _activeShells; i++)
            {
                Shell shell = _shells[i];
                if (shell.Source == null)
                {
                    shell.Renderer.enabled = false;
                    continue;
                }

                shell.Transform.SetPositionAndRotation(shell.Source.position, shell.Source.rotation);
                shell.Transform.localScale = shell.Source.lossyScale * shell.ScaleFactor;
            }
        }

        private void OnDisable() => Retarget(null);

        private void OnDestroy()
        {
            // The shells are parentless, so nothing else would clean them up along with the player.
            for (int i = 0; i < _shells.Count; i++)
                if (_shells[i].Transform != null)
                    Destroy(_shells[i].Transform.gameObject);
            _shells.Clear();
            _activeShells = 0;
        }

        // Carry first: aiming at a prop is a deliberate act, and it is the only thing a grab can act on.
        private Transform ResolveTarget()
        {
            if (_carry != null)
            {
                Rigidbody hover = _carry.HoverTarget;
                if (hover != null)
                    return hover.transform;
            }

            IInteractable active = _interactor != null ? _interactor.Current : null;
            return active != null ? active.transform : null;
        }

        private void Retarget(Transform target)
        {
            _current = target;

            for (int i = 0; i < _activeShells; i++)
            {
                _shells[i].Renderer.enabled = false;
                _shells[i].Source = null;
            }
            _activeShells = 0;

            if (_current == null)
                return;

            MeshFilter[] sources = _current.GetComponentsInChildren<MeshFilter>();
            for (int i = 0; i < sources.Length; i++)
            {
                MeshFilter source = sources[i];
                if (source.sharedMesh == null)
                    continue;

                var sourceRenderer = source.GetComponent<Renderer>();
                if (sourceRenderer == null || !sourceRenderer.enabled)
                    continue;

                Shell shell = ShellAt(_activeShells++);

                // Scale the shell so the border is a constant world width regardless of mesh size: a
                // factor of 1 + width/extent adds roughly _outlineWidth beyond the surface on every axis.
                Vector3 ext = sourceRenderer.bounds.extents;
                float extent = Mathf.Max((ext.x + ext.y + ext.z) / 3f, 0.001f);
                shell.ScaleFactor = 1f + _outlineWidth / extent;

                shell.Filter.sharedMesh = source.sharedMesh;
                shell.Source = source.transform;
                shell.Renderer.enabled = true;
            }
        }

        private Shell ShellAt(int index)
        {
            while (_shells.Count <= index)
                _shells.Add(BuildShell(_shells.Count));
            return _shells[index];
        }

        private Shell BuildShell(int index)
        {
            var go = new GameObject("_HoverOutline_" + index);
            var shell = new Shell
            {
                Filter = go.AddComponent<MeshFilter>(),
                Renderer = go.AddComponent<MeshRenderer>(),
                Transform = go.transform,
            };
            shell.Renderer.sharedMaterial = _outlineMaterial;
            shell.Renderer.shadowCastingMode = ShadowCastingMode.Off;
            shell.Renderer.receiveShadows = false;
            shell.Renderer.lightProbeUsage = LightProbeUsage.Off;
            shell.Renderer.reflectionProbeUsage = ReflectionProbeUsage.Off;
            shell.Renderer.enabled = false;
            return shell;
        }
    }
}
