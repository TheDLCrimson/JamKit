using System;
using UnityEngine;
using UnityEngine.Rendering;

// AnalogFXVolume: the gameplay-facing contract for the analog/CRT look.
//
// This is the ONLY thing gameplay code and Volume Profile presets ever talk to. Every parameter is
// 0..1 and means "how much", never "which shader" or "what colour". Artist C replaces the shader and
// material behind it; this file should not need to change when the real art lands.
//
// Authoring: add this override to a Volume Profile and set the four values. Blending between
// profiles is handled by URP's Volume system for free.
[Serializable]
[VolumeComponentMenu("JamKit/Analog FX")]
public sealed class AnalogFXVolume : VolumeComponent
{
    [Tooltip("Horizontal CRT scanline banding. 0 = none, 1 = heavy.")]
    public ClampedFloatParameter scanline = new ClampedFloatParameter(0f, 0f, 1f);

    [Tooltip("Film/sensor grain. 0 = clean, 1 = heavy noise.")]
    public ClampedFloatParameter grain = new ClampedFloatParameter(0f, 0f, 1f);

    [Tooltip("Chromatic aberration: RGB channel separation. 0 = aligned, 1 = heavy fringing.")]
    public ClampedFloatParameter chromaticOffset = new ClampedFloatParameter(0f, 0f, 1f);

    [Tooltip("Signal glitch: blocky row tearing. 0 = stable, 1 = badly broken. This is the parameter gameplay animates at runtime.")]
    public ClampedFloatParameter glitch = new ClampedFloatParameter(0f, 0f, 1f);

    /// <summary>True when any parameter would produce a visible change.</summary>
    public bool IsActive() =>
        scanline.value > 0f || grain.value > 0f || chromaticOffset.value > 0f || glitch.value > 0f;
}
