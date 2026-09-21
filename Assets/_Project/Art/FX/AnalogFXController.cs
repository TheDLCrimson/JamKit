using JamKit;
using UnityEngine;
using UnityEngine.Rendering;

// AnalogFXController: the bridge from the Volume stack to the shader, plus the kill switch.
//
// Deliberately the whole driver layer. Three parameters (scanline, grain, chromaticOffset) are
// authored in Volume Profile presets and pushed straight through. Only `glitch` is animated at
// runtime, because it is the only one gameplay modulates; adding drivers for the others would be
// machinery with no caller.
//
// Values reach the shader as GLOBAL shader uniforms, not material properties. That is on purpose:
//   - nothing ever writes to the shared material asset, so the ProjectFPS StanceVignette bug
//     (runtime edits persisting into the asset) cannot happen here;
//   - swapping the material or shader needs no code change and no re-wiring.
// The cost is that the material inspector's sliders do nothing. Values come from the Volume stack.
// [ExecuteAlways] is required, not decorative: without it the globals are never pushed outside Play
// mode, so _AnalogFX_Enabled stays 0, the shader passes through, and the effect is invisible in the
// Scene and Game views. Preset tuning would mean entering Play mode for every change.
[ExecuteAlways]
[DefaultExecutionOrder(100)]
public class AnalogFXController : MonoBehaviour
{
    // Shader uniform names: THE CONTRACT. A replacement shader must declare these same names.
    private static readonly int EnabledId = Shader.PropertyToID("_AnalogFX_Enabled");
    private static readonly int ScanlineId = Shader.PropertyToID("_AnalogFX_Scanline");
    private static readonly int GrainId = Shader.PropertyToID("_AnalogFX_Grain");
    private static readonly int ChromaticId = Shader.PropertyToID("_AnalogFX_Chromatic");
    private static readonly int GlitchId = Shader.PropertyToID("_AnalogFX_Glitch");

    [Header("Kill switch")]
    [Tooltip("Off = the shader passes the image through untouched. For a hard disable with zero GPU cost, untick the Full Screen Pass feature on BOTH PC_Renderer and Mobile_Renderer.")]
    [SerializeField] private bool _effectEnabled = true;

    [Header("Runtime-animated parameter")]
    [Tooltip("Smoothing rate for glitch. Higher snaps faster.")]
    [SerializeField] private float _glitchResponse = 12f;

    private float _glitchTarget;
    private float _glitchCurrent;

    /// <summary>Kill switch. False makes the pass a pass-through; the presets are left untouched.</summary>
    public bool EffectEnabled
    {
        get => _effectEnabled;
        set { _effectEnabled = value; Push(); }
    }

    /// <summary>
    /// The one runtime-animated parameter: extra glitch on top of whatever the active Volume
    /// Profile authored. Clamped to 0..1 after summing.
    /// </summary>
    public void SetGlitch01(float glitch01) => _glitchTarget = Mathf.Clamp01(glitch01);

    private void OnEnable()
    {
        Push();
#if UNITY_EDITOR
        // [ExecuteAlways] Update() only ticks when the Editor repaints, so the preview goes stale
        // while the Editor is idle. Drive it explicitly instead.
        UnityEditor.EditorApplication.update += EditorTick;
#endif
    }

    private void OnDisable()
    {
#if UNITY_EDITOR
        UnityEditor.EditorApplication.update -= EditorTick;
#endif
        // Leave the global state clean so a disabled controller never leaves the screen glitched.
        Shader.SetGlobalFloat(EnabledId, 0f);
    }

#if UNITY_EDITOR
    private void EditorTick()
    {
        if (this == null || Application.isPlaying)
            return;
        Push();
    }
#endif

    private void Update()
    {
        // Outside Play mode there is no meaningful frame delta and nothing animates glitch, so skip
        // the smoothing and just publish the authored preset values for live preview.
        if (Application.isPlaying)
            _glitchCurrent = MathUtil.Decay(_glitchCurrent, _glitchTarget, _glitchResponse, Time.deltaTime);
        else
            _glitchCurrent = 0f;
        Push();
    }

    private void Push()
    {
        Shader.SetGlobalFloat(EnabledId, _effectEnabled ? 1f : 0f);
        if (!_effectEnabled)
            return;

        // In Play mode the camera drives the Volume stack. In Edit mode nothing does, so the stack
        // would read all zeros and the preview would be blank; drive it here for live preview.
        if (!Application.isPlaying)
            VolumeManager.instance.Update(transform, ~0);

        AnalogFXVolume fx = VolumeManager.instance.stack?.GetComponent<AnalogFXVolume>();
        float scanline = fx != null ? fx.scanline.value : 0f;
        float grain = fx != null ? fx.grain.value : 0f;
        float chromatic = fx != null ? fx.chromaticOffset.value : 0f;
        float glitch = fx != null ? fx.glitch.value : 0f;

        Shader.SetGlobalFloat(ScanlineId, scanline);
        Shader.SetGlobalFloat(GrainId, grain);
        Shader.SetGlobalFloat(ChromaticId, chromatic);
        Shader.SetGlobalFloat(GlitchId, Mathf.Clamp01(glitch + _glitchCurrent));
    }

#if UNITY_EDITOR
    private void OnValidate() => Push();
#endif
}
