using JamKit;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

// Drives the Demo_Feel scene: oscillates a camera rig to exercise CameraSpring + CameraLean, pulses a URP
// Vignette via VolumeOverrideDriver, click-to-explode a PartExploder, toggles a UIPanelAnimator panel, and
// registers DebugOverlay cheat buttons. Plain Assembly-CSharp, no namespace (jam-code convention).
public class DemoFeelController : MonoBehaviour
{
    [Header("Camera feel rig")]
    [SerializeField] private Transform _cameraRig;
    [SerializeField] private CameraLean _cameraLean;
    [SerializeField] private CameraSpring _cameraSpring;
    [SerializeField] private float _rigTravel = 4f;
    [SerializeField] private float _rigSpeed = 1.5f;

    [Header("Volume")]
    [SerializeField] private Volume _volume;
    [SerializeField] private VolumeOverrideDriver _volumeDriver;
    [SerializeField] private float _accelToVignette = 40f;

    [Header("Breakable")]
    [SerializeField] private PartExploder _breakable;
    [SerializeField] private Camera _rayCamera;

    [Header("Juice")]
    [SerializeField] private FloatingCursor _floatingCursor;

    [Header("UI")]
    [SerializeField] private UIPanelAnimator _infoPanel;

    private Vector3 _rigBasePos;
    private Vector3 _prevPos;
    private Vector3 _prevVelocity;

    private void Start()
    {
        if (_cameraSpring) _cameraSpring.Initialize();
        if (_cameraLean) _cameraLean.Initialize();

        if (_cameraRig)
        {
            _rigBasePos = _cameraRig.position;
            _prevPos = _cameraRig.position;
        }

        if (_volume && _volumeDriver)
        {
            _volumeDriver.Initialize<Vignette>(_volume, v => v.intensity, 0.15f, 0.5f, 8f);
        }

        if (_floatingCursor) _floatingCursor.StartAnimation();
        if (_infoPanel) _infoPanel.Show();

#if UNITY_EDITOR || DEVELOPMENT_BUILD
        // Fully qualified to disambiguate from UnityEngine.Rendering.DebugOverlay.
        JamKit.DebugOverlay.AddButton("Explode Breakable", () => { if (_breakable) _breakable.Explode(); });
        JamKit.DebugOverlay.AddButton("Toggle Info Panel", () => { if (_infoPanel) _infoPanel.Toggle(); });
        JamKit.DebugOverlay.AddButton("Damage Flash", () => { if (UIScreenEffects.Instance) UIScreenEffects.Instance.DamageFlash(); });
        JamKit.DebugOverlay.AddButton("Fade Out+In", () =>
        {
            const float fadeCycleDuration = 0.6f;
            if (UIScreenEffects.Instance) UIScreenEffects.Instance.FadeOut(fadeCycleDuration, onComplete: () => UIScreenEffects.Instance.FadeIn(fadeCycleDuration));
        });
#endif
    }

    private void Update()
    {
        float dt = Time.deltaTime;

        if (_cameraRig)
        {
            float x = Mathf.Sin(Time.time * _rigSpeed) * _rigTravel;
            _cameraRig.position = _rigBasePos + new Vector3(x, 0f, 0f);

            Vector3 velocity = (_cameraRig.position - _prevPos) / Mathf.Max(dt, 1e-4f);
            Vector3 acceleration = (velocity - _prevVelocity) / Mathf.Max(dt, 1e-4f);
            _prevPos = _cameraRig.position;
            _prevVelocity = velocity;

            if (_cameraLean) _cameraLean.UpdateLean(dt, acceleration, Vector3.up);
            if (_cameraSpring) _cameraSpring.UpdateSpring(dt, Vector3.up);
            if (_volumeDriver && _volumeDriver.IsInitialized)
            {
                _volumeDriver.SetTarget01(Mathf.Clamp01(acceleration.magnitude / Mathf.Max(_accelToVignette, 0.01f)));
            }
        }

        if (Mouse.current != null && Mouse.current.leftButton.wasPressedThisFrame && _rayCamera)
        {
            Ray ray = _rayCamera.ScreenPointToRay(Mouse.current.position.ReadValue());
            if (Physics.Raycast(ray, out RaycastHit hit))
            {
                PartExploder exploder = hit.collider.GetComponentInParent<PartExploder>();
                if (exploder) exploder.Explode();
            }
        }
    }
}
